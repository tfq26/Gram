using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class SandboxSchedulingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gram_leases_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Lease_IsPersistedHeartbeatedAndReleased()
    {
        var storage = CreateStorage();
        var scheduler = new SandboxScheduler(storage, NullLogger<SandboxScheduler>.Instance);
        var lease = await scheduler.AcquireAsync(new SandboxRequest("run-1", SandboxResourceClass.Tiny));
        Assert.Single(scheduler.GetLeases());
        var originalExpiry = lease.ExpiresAt;

        scheduler.Heartbeat(lease.Id);
        Assert.True(scheduler.GetLeases().Single().ExpiresAt >= originalExpiry);
        scheduler.Release(lease.Id);

        Assert.Empty(scheduler.GetLeases());
        var leaseDir = Path.Combine(_root, "sandbox-leases");
        Assert.True(!Directory.Exists(leaseDir) || !Directory.EnumerateFiles(leaseDir).Any());
    }

    [Fact]
    public void ExpiredLeaseRecord_IsRecoveredOnStartup()
    {
        var storage = CreateStorage();
        var directory = storage.GetFilePath("sandbox-leases");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "expired.json"), "{\"id\":\"expired\",\"expiresAt\":\"2020-01-01T00:00:00Z\"}");

        var scheduler = new SandboxScheduler(storage, NullLogger<SandboxScheduler>.Instance);

        Assert.Empty(scheduler.GetLeases());
        Assert.False(File.Exists(Path.Combine(directory, "expired.json")));
    }

    [Fact]
    public async Task TenIdleSandboxes_DoNotConsumeExecutionSlots()
    {
        var scheduler = new SandboxScheduler(CreateStorage(), NullLogger<SandboxScheduler>.Instance);
        var leases = await Task.WhenAll(Enumerable.Range(1, 10).Select(index => scheduler.AcquireAsync(new SandboxRequest($"run-{index}"))));

        Assert.Equal(10, scheduler.ActiveLeaseCount);
        Assert.Equal(0, scheduler.ActiveExecutionCount);
        foreach (var lease in leases) scheduler.Release(lease.Id);
    }

    private StorageService CreateStorage() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataDir"] = _root }).Build());

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
