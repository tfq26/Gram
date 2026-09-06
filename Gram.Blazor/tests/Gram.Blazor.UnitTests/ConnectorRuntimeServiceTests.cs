using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ConnectorRuntimeServiceTests
{
    [Fact]
    public async Task RuntimeStateRestoresAsUnhealthyAfterRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "gram-runtime-test-" + Guid.NewGuid().ToString("N"));
        var packagePath = Path.Combine(root, "package");
        Directory.CreateDirectory(packagePath);
        File.WriteAllText(Path.Combine(packagePath, "Connector.Validation.dll"), "placeholder");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GRAM_DATA_DIR"] = root }).Build();
            var storage = new StorageService(configuration);
            var manifest = new ConnectorManifest("generated", "Generated", "1.0.0", ConnectorTransport.Mcp, "", [], [], ExecutionPolicy: new ConnectorExecutionPolicy([]));
            var package = new ConnectorPackageRecord("generated-1.0.0", "1.0.0", ConnectorPackageStatus.Installed, manifest, packagePath, [], DateTimeOffset.UtcNow, true, "owner-1");
            var packages = new FakePackages(package);

            var first = new ConnectorRuntimeService(packages, new ConnectorRegistry([]), new FakeHost(), storage);
            var activated = await first.ActivateAsync(package.Id);
            Assert.Equal(ConnectorRuntimeStatus.Healthy, activated.Status);

            var restarted = new ConnectorRuntimeService(packages, new ConnectorRegistry([]), new FakeHost(), storage);
            var restored = Assert.Single(restarted.List());
            Assert.Equal(ConnectorRuntimeStatus.Unhealthy, restored.Status);
            Assert.Contains("reactivation", restored.Error ?? "", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakePackages(ConnectorPackageRecord package) : IConnectorPackageService
    {
        public Task<ConnectorPackageRecord> StageAsync(ConnectorGenerationPreview preview, CancellationToken cancellationToken = default, string ownerId = "default") => throw new NotImplementedException();
        public Task<ConnectorPackageRecord> CompileAsync(string packageId, CancellationToken cancellationToken = default, string? ownerId = null) => throw new NotImplementedException();
        public Task<ConnectorPackageRecord> InstallAsync(string packageId, bool approved, CancellationToken cancellationToken = default, string? ownerId = null) => throw new NotImplementedException();
        public Task<IReadOnlyList<ConnectorPackageRecord>> ListAsync(CancellationToken cancellationToken = default, string? ownerId = null) => Task.FromResult<IReadOnlyList<ConnectorPackageRecord>>([package]);
        public Task<ConnectorPackageRecord?> GetAsync(string packageId, CancellationToken cancellationToken = default, string? ownerId = null) => Task.FromResult<ConnectorPackageRecord?>(package.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase) ? package : null);
    }

    private sealed class FakeHost : IConnectorHostClient
    {
        public Task<ConnectorActionResult> ExecuteAsync(ConnectorHostExecuteRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new ConnectorActionResult(true, "ok"));
    }
}
