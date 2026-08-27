namespace Gram.Blazor.Shared;

public enum SandboxResourceClass { Tiny, Small, Medium, Large }

public sealed record SandboxResourceBudget(SandboxResourceClass Class, int CpuCores, long MemoryBytes, long DiskBytes);

public sealed record SandboxRequest(string? RunId = null, SandboxResourceClass ResourceClass = SandboxResourceClass.Small);

public sealed class SandboxLease
{
    public string Id { get; init; } = "lease-" + Guid.NewGuid().ToString("N")[..12];
    public string? RunId { get; init; }
    public SandboxResourceBudget Budget { get; init; } = SandboxResourceCatalog.For(SandboxResourceClass.Small);
    public DateTimeOffset AcquiredAt { get; init; } = DateTimeOffset.UtcNow;
}

public interface ISandboxScheduler
{
    int ActiveLeaseCount { get; }
    Task<SandboxLease> AcquireAsync(SandboxRequest request, CancellationToken cancellationToken = default);
    void Release(string leaseId);
}

public static class SandboxResourceCatalog
{
    public static SandboxResourceBudget For(SandboxResourceClass resourceClass) => resourceClass switch
    {
        SandboxResourceClass.Tiny => new(resourceClass, 1, 1024L * 1024 * 1024, 2L * 1024 * 1024 * 1024),
        SandboxResourceClass.Medium => new(resourceClass, 4, 8L * 1024 * 1024 * 1024, 10L * 1024 * 1024 * 1024),
        SandboxResourceClass.Large => new(resourceClass, 8, 16L * 1024 * 1024 * 1024, 25L * 1024 * 1024 * 1024),
        _ => new(resourceClass, 2, 2L * 1024 * 1024 * 1024, 5L * 1024 * 1024 * 1024)
    };
}

public sealed class SandboxScheduler : ISandboxScheduler, IDisposable
{
    private readonly SemaphoreSlim _slots = new(Math.Max(1, Environment.ProcessorCount - 1));
    private readonly Dictionary<string, SandboxLease> _leases = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    public int ActiveLeaseCount { get { lock (_sync) return _leases.Count; } }

    public async Task<SandboxLease> AcquireAsync(SandboxRequest request, CancellationToken cancellationToken = default)
    {
        await _slots.WaitAsync(cancellationToken);
        var lease = new SandboxLease { RunId = request.RunId, Budget = SandboxResourceCatalog.For(request.ResourceClass) };
        lock (_sync) _leases[lease.Id] = lease;
        return lease;
    }

    public void Release(string leaseId)
    {
        lock (_sync)
        {
            if (!_leases.Remove(leaseId)) return;
            _slots.Release();
        }
    }

    public void Dispose() => _slots.Dispose();
}
