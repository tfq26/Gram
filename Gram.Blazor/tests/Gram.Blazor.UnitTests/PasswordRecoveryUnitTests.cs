using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gram.Blazor.UnitTests;

public class PasswordRecoveryUnitTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StorageService _storageService;
    private readonly SqliteDatabaseService _db;
    private readonly GramAuthService _authService;

    public PasswordRecoveryUnitTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "gram_recovery_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Environment.SetEnvironmentVariable("GRAM_STORAGE_DIR", _tempDir);

        var config = new ConfigurationBuilder().Build();
        _storageService = new StorageService();
        _db = new SqliteDatabaseService(_storageService);
        _db.InitializeAsync().GetAwaiter().GetResult();

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
    public async Task RequestAndCompletePasswordReset_WorksEndToEnd()
    {
        // 1. Sign up user
        var signUpRes = await _authService.SignUpWithEmailAsync("Bob Test", "bob@example.com", "OldPassword123!");
        Assert.True(signUpRes.Success);

        // 2. Request password reset
        var reqRes = await _authService.RequestPasswordResetAsync("bob@example.com", "http://localhost:7337");
        Assert.True(reqRes.Success);
        Assert.NotNull(reqRes.DevCode);
        Assert.Equal(6, reqRes.DevCode.Length);

        // 3. Complete password reset with the 6-digit code
        var resetRes = await _authService.CompletePasswordResetAsync("bob@example.com", reqRes.DevCode, "NewBrandPassword456!");
        Assert.True(resetRes.Success);
        Assert.NotNull(resetRes.User);
        Assert.True(resetRes.User.IsAuthenticated);

        // 4. Attempt login with old password -> should fail
        await _authService.LogoutAsync();
        var oldLogin = await _authService.SignInWithEmailAsync("bob@example.com", "OldPassword123!");
        Assert.False(oldLogin.Success);

        // 5. Attempt login with new password -> should succeed
        var newLogin = await _authService.SignInWithEmailAsync("bob@example.com", "NewBrandPassword456!");
        Assert.True(newLogin.Success);
        Assert.Equal("bob@example.com", newLogin.User?.Email);
    }

    [Fact]
    public async Task CompletePasswordReset_RejectsUsedOrInvalidCode()
    {
        // 1. Create user
        await _authService.SignUpWithEmailAsync("Charlie", "charlie@example.com", "Password123!");

        // 2. Request reset
        var reqRes = await _authService.RequestPasswordResetAsync("charlie@example.com");
        Assert.True(reqRes.Success);
        var code = reqRes.DevCode!;

        // 3. Attempt invalid code
        var invalidRes = await _authService.CompletePasswordResetAsync("charlie@example.com", "000000", "NewPass123!");
        Assert.False(invalidRes.Success);

        // 4. Use valid code
        var validRes = await _authService.CompletePasswordResetAsync("charlie@example.com", code, "NewPass123!");
        Assert.True(validRes.Success);

        // 5. Attempt to reuse already used code -> should fail
        var reusedRes = await _authService.CompletePasswordResetAsync("charlie@example.com", code, "AnotherPass123!");
        Assert.False(reusedRes.Success);
        Assert.Contains("already been used", reusedRes.ErrorMessage ?? "");
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
