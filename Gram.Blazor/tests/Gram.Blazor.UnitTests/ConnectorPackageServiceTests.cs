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
            Assert.Equal(ConnectorPackageStatus.Invalid, staged.Status);
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

    [Fact]
    public async Task PackageRecordsAreIsolatedByOwner()
    {
        var root = Path.Combine(Path.GetTempPath(), "gram-connector-owner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var old = Environment.GetEnvironmentVariable("GRAM_DATA_DIR");
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", root);
        try
        {
            var sdk = new ConnectorSdk();
            var preview = await sdk.GeneratePreviewAsync(new ConnectorGenerationRequest("Owned Service", "Owned connector"));
            var packages = new ConnectorPackageService(sdk);
            var staged = await packages.StageAsync(preview, ownerId: "owner-a");

            Assert.NotNull(await packages.GetAsync(staged.Id, ownerId: "owner-a"));
            Assert.Null(await packages.GetAsync(staged.Id, ownerId: "owner-b"));
            Assert.Empty(await packages.ListAsync(ownerId: "owner-b"));

            // Cross-owner compile & install attempts must fail with KeyNotFoundException
            await Assert.ThrowsAsync<KeyNotFoundException>(() => packages.CompileAsync(staged.Id, ownerId: "owner-b"));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => packages.InstallAsync(staged.Id, approved: true, ownerId: "owner-b"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GRAM_DATA_DIR", old);
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
