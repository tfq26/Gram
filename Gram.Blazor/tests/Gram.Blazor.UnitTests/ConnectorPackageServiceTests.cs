using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ConnectorPackageServiceTests
{
    [Fact]
    public async Task StagePersistsValidatedPackageAndInstallRequiresCompilationAndApproval()
    {
        var root = Path.Combine(Path.GetTempPath(), "gram-connector-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var old = Environment.GetEnvironmentVariable("GRAM_DATA_DIR");
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", root);
        try
        {
            var sdk = new ConnectorSdk();
            var preview = await sdk.GeneratePreviewAsync(new ConnectorGenerationRequest("Example Service", "Example connector"));
            var packages = new ConnectorPackageService(sdk);
            var staged = await packages.StageAsync(preview);
            Assert.Equal(ConnectorPackageStatus.Staged, staged.Status);
            Assert.True(File.Exists(Path.Combine(staged.PackagePath, "connector.json")));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => packages.InstallAsync(staged.Id, false));
            await Assert.ThrowsAsync<InvalidOperationException>(() => packages.InstallAsync(staged.Id, true));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GRAM_DATA_DIR", old);
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
