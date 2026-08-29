using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class PasskeyAuthUnitTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StorageService _storageService;
    private readonly SqliteDatabaseService _db;
    private readonly GramAuthService _authService;

    public PasskeyAuthUnitTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "gram_passkey_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Environment.SetEnvironmentVariable("GRAM_STORAGE_DIR", _tempDir);

        var config = new ConfigurationBuilder().Build();
        _storageService = new StorageService();
        _db = new SqliteDatabaseService(_storageService);

        var httpClientFactory = new TestHttpClientFactory();
        _authService = new GramAuthService(config, httpClientFactory, _storageService, _db);
        _authService.Settings.Provider = AuthProviderType.None;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch {}
    }

    [Fact]
    public async Task Passkey_RegisterAndSignIn_WorksEndToEnd()
    {
        // 1. Create a user via sign up
        var signUpRes = await _authService.SignUpWithEmailAsync("Alice Passkey", "alice@passkey.test", "SecurePass123!");
        Assert.True(signUpRes.Success);
        Assert.NotNull(signUpRes.User);
        Assert.False(signUpRes.User.HasPasskey);

        // 2. Register a Passkey
        var credentialId = "cred_" + Guid.NewGuid().ToString("N");
        var publicKey = "pk_" + Guid.NewGuid().ToString("N");
        var regRes = await _authService.RegisterPasskeyAsync(credentialId, publicKey, "Touch ID (Mac)");

        Assert.True(regRes.Success);
        Assert.True(_authService.CurrentUser.HasPasskey);

        // 3. List Passkeys
        var passkeys = await _authService.GetPasskeysAsync();
        Assert.Single(passkeys);
        Assert.Equal(credentialId, passkeys[0].Id);
        Assert.Equal("Touch ID (Mac)", passkeys[0].Name);

        // 4. Log out
        await _authService.LogoutAsync();
        Assert.False(_authService.CurrentUser.IsAuthenticated);

        // 5. Sign in with Passkey
        var passkeyLoginRes = await _authService.SignInWithPasskeyAsync(credentialId);
        Assert.True(passkeyLoginRes.Success);
        Assert.NotNull(passkeyLoginRes.User);
        Assert.Equal("alice@passkey.test", passkeyLoginRes.User.Email);
        Assert.True(passkeyLoginRes.User.IsAuthenticated);
        Assert.True(passkeyLoginRes.User.HasPasskey);

        // 6. Delete Passkey
        var deleteRes = await _authService.DeletePasskeyAsync(credentialId);
        Assert.True(deleteRes);

        var remainingPasskeys = await _authService.GetPasskeysAsync();
        Assert.Empty(remainingPasskeys);
        Assert.False(_authService.CurrentUser.HasPasskey);
    }

    [Fact]
    public async Task Passkey_SignInWithUnknownCredential_ReturnsFailure()
    {
        var result = await _authService.SignInWithPasskeyAsync("non_existent_cred_id");
        Assert.False(result.Success);
        Assert.Contains("not recognized", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
