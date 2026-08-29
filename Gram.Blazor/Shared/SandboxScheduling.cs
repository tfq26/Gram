using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public enum SandboxResourceClass { Tiny, Small, Medium, Large }

public sealed record SandboxResourceBudget(SandboxResourceClass Class, int CpuCores, long MemoryBytes, long DiskBytes);

public sealed record SandboxRequest(string? RunId = null, SandboxResourceClass ResourceClass = SandboxResourceClass.Small, SandboxNetworkMode NetworkMode = SandboxNetworkMode.Disabled, IReadOnlyList<string>? AllowedNetworkHosts = null);

public sealed class SandboxLease
{
    public string Id { get; init; } = "lease-" + Guid.NewGuid().ToString("N")[..12];
    public string? RunId { get; init; }
    public SandboxResourceBudget Budget { get; init; } = SandboxResourceCatalog.For(SandboxResourceClass.Small);
    public DateTimeOffset AcquiredAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastHeartbeatAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(30);
    public SandboxNetworkMode NetworkMode { get; init; } = SandboxNetworkMode.Disabled;
    public List<string> AllowedNetworkHosts { get; init; } = [];
}

public interface ISandboxScheduler
{
    int ActiveLeaseCount { get; }
    int ActiveExecutionCount { get; }
    Task<SandboxLease> AcquireAsync(SandboxRequest request, CancellationToken cancellationToken = default);
    Task<IDisposable> AcquireExecutionAsync(SandboxResourceBudget budget, CancellationToken cancellationToken = default);
    void Heartbeat(string leaseId);
    void Release(string leaseId);
    IReadOnlyList<SandboxLease> GetLeases();
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
    private readonly SemaphoreSlim _executionSlots = new(Math.Max(1, Environment.ProcessorCount - 1));
    private readonly Dictionary<string, SandboxLease> _leases = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly string _leaseDirectory;
    private readonly ILogger<SandboxScheduler>? _logger;
    public int ActiveLeaseCount { get { lock (_sync) return _leases.Count; } }
    public int ActiveExecutionCount { get { lock (_sync) return _executionCount; } }
    private int _executionCount;

    public SandboxScheduler(StorageService? storage = null, ILogger<SandboxScheduler>? logger = null)
    {
        _leaseDirectory = storage?.GetFilePath("sandbox-leases") ?? Path.Combine(Path.GetTempPath(), "gram-sandbox-leases");
        _logger = logger;
        Directory.CreateDirectory(_leaseDirectory);
        RecoverExpiredLeases();
    }

    public Task<SandboxLease> AcquireAsync(SandboxRequest request, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var lease = new SandboxLease { RunId = request.RunId, Budget = SandboxResourceCatalog.For(request.ResourceClass), LastHeartbeatAt = now, ExpiresAt = now.AddMinutes(30), NetworkMode = request.NetworkMode, AllowedNetworkHosts = request.AllowedNetworkHosts?.ToList() ?? [] };
        lock (_sync) { _leases[lease.Id] = lease; Persist(lease); }
        return Task.FromResult(lease);
    }

    public async Task<IDisposable> AcquireExecutionAsync(SandboxResourceBudget budget, CancellationToken cancellationToken = default)
    {
        await _executionSlots.WaitAsync(cancellationToken);
        lock (_sync) _executionCount++;
        return new ExecutionReservation(this);
    }

    public void Heartbeat(string leaseId)
    {
        lock (_sync)
        {
            if (!_leases.TryGetValue(leaseId, out var lease)) return;
            lease.LastHeartbeatAt = DateTimeOffset.UtcNow;
            lease.ExpiresAt = lease.LastHeartbeatAt.AddMinutes(30);
            Persist(lease);
        }
    }

    public void Release(string leaseId)
    {
        lock (_sync)
        {
            if (!_leases.Remove(leaseId)) return;
            try { File.Delete(Path.Combine(_leaseDirectory, $"{leaseId}.json")); } catch { }
        }
    }

    private sealed class ExecutionReservation(SandboxScheduler owner) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            lock (owner._sync) owner._executionCount--;
            owner._executionSlots.Release();
        }
    }

    public IReadOnlyList<SandboxLease> GetLeases()
    {
        lock (_sync) return _leases.Values.Select(lease => new SandboxLease { Id = lease.Id, RunId = lease.RunId, Budget = lease.Budget, AcquiredAt = lease.AcquiredAt, LastHeartbeatAt = lease.LastHeartbeatAt, ExpiresAt = lease.ExpiresAt, NetworkMode = lease.NetworkMode, AllowedNetworkHosts = lease.AllowedNetworkHosts.ToList() }).ToList();
    }

    private void Persist(SandboxLease lease)
    {
        try { File.WriteAllText(Path.Combine(_leaseDirectory, $"{lease.Id}.json"), JsonSerializer.Serialize(lease, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Could not persist sandbox lease {LeaseId}.", lease.Id); }
    }

    private void RecoverExpiredLeases()
    {
        foreach (var path in Directory.EnumerateFiles(_leaseDirectory, "*.json"))
        {
            try
            {
                var lease = JsonSerializer.Deserialize<SandboxLease>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (lease is null || lease.ExpiresAt <= DateTimeOffset.UtcNow)
                {
                    File.Delete(path);
                    _logger?.LogInformation("Recovered expired sandbox lease record {Path}.", path);
                }
            }
            catch { try { File.Delete(path); } catch { } }
        }
    }

    public void Dispose() => _executionSlots.Dispose();
}
