using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ConnectorCredentialVaultTests
{
    [Fact]
    public async Task StoresResolvesAndDeletesCredentialsByExactOwnerScope()
    {
        var (directory, storage) = CreateStorage();
        try
        {
            var vault = new ConnectorCredentialVault(storage);
            var reference = new ConnectorCredentialReference("user-1", "provider", "connection-1", "api_key");
            await vault.StoreAsync(reference, "secret-value");

            Assert.Equal("secret-value", await vault.ResolveAsync(reference, ["api_key"]));
            Assert.Null(await vault.ResolveAsync(reference, ["other"]));
            Assert.True(await vault.DeleteConnectionAsync("user-1", "provider", "connection-1"));
            Assert.Null(await vault.ResolveAsync(reference, ["api_key"]));
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public async Task TamperedVaultFailsClosedInsteadOfReturningEmptyState()
    {
        var (directory, storage) = CreateStorage();
        try
        {
            var vault = new ConnectorCredentialVault(storage);
            var reference = new ConnectorCredentialReference("user-1", "provider", "connection-1", "api_key");
            await vault.StoreAsync(reference, "secret-value");
            File.WriteAllBytes(storage.GetFilePath("connector_credentials.dat"), [1, 2, 3]);

            await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(() => vault.ResolveAsync(reference, ["api_key"]));
        }
        finally { DeleteDirectory(directory); }
    }

    private static (string Directory, StorageService Storage) CreateStorage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gram-vault-test-" + Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GRAM_DATA_DIR"] = directory })
            .Build();
        return (directory, new StorageService(configuration));
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
