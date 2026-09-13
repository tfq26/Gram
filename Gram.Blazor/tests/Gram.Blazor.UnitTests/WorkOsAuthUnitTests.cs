using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gram.Blazor.UnitTests;

public class WorkOsAuthUnitTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StorageService _storageService;
    private readonly SqliteDatabaseService _db;

    public WorkOsAuthUnitTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "gram_workos_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Environment.SetEnvironmentVariable("GRAM_STORAGE_DIR", _tempDir);

        _storageService = new StorageService();
        _db = new SqliteDatabaseService(_storageService);
        _db.InitializeAsync().GetAwaiter().GetResult();
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
        catch { }
    }

    [Fact]
    public void WorkOs_NotConfigured_DefaultsToGuestMode()
    {
        var config = new ConfigurationBuilder().Build();
        var httpClientFactory = new CustomMockHttpClientFactory(new HttpClient());
        var authService = new GramAuthService(config, httpClientFactory, _storageService, _db);

        Assert.False(authService.IsWorkOsConfigured);
        Assert.False(authService.CurrentUser.IsAuthenticated);
        Assert.Equal("Guest", authService.CurrentUser.Name);
    }

    [Fact]
    public void WorkOs_Configured_DetectsCredentialsAndBuildsAuthorizeUrl()
    {
        var inMemory = new System.Collections.Generic.Dictionary<string, string?>
        {
            ["WORKOS_API_KEY"] = "sk_test_123456789",
            ["WORKOS_CLIENT_ID"] = "client_01JABCDEF",
            ["WORKOS_REDIRECT_URI"] = "https://gram.2helix.org/auth/callback"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var httpClientFactory = new CustomMockHttpClientFactory(new HttpClient());
        var authService = new GramAuthService(config, httpClientFactory, _storageService, _db);

        Assert.True(authService.IsWorkOsConfigured);
        Assert.Equal(AuthProviderType.WorkOS, authService.Settings.Provider);

        var loginUrl = authService.GetWorkOsLoginUrl("/settings");
        Assert.Contains("https://api.workos.com/user_management/authorize", loginUrl);
        Assert.Contains("client_id=client_01JABCDEF", loginUrl);
        Assert.Contains("redirect_uri=https%3A%2F%2Fgram.2helix.org%2Fauth%2Fcallback", loginUrl);
        Assert.Contains("response_type=code", loginUrl);
        Assert.Contains("state=%2Fsettings", loginUrl);
    }

    [Fact]
    public async Task WorkOs_HandleCallbackAsync_Success_EstablishesUserSession()
    {
        var inMemory = new System.Collections.Generic.Dictionary<string, string?>
        {
            ["WORKOS_API_KEY"] = "sk_test_mock_secret",
            ["WORKOS_CLIENT_ID"] = "client_mock_id",
            ["WORKOS_REDIRECT_URI"] = "https://gram.2helix.org/auth/callback"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var mockWorkOsResponse = new WorkOsAuthenticateResponse
        {
            AccessToken = "jwt_mock_token_workos_123",
            User = new WorkOsUser
            {
                Id = "user_01JXYZ987",
                Email = "alice@2helix.org",
                FirstName = "Alice",
                LastName = "Lovelace",
                EmailVerified = true,
                ProfilePictureUrl = "https://images.example.com/avatar.png"
            }
        };

        var handler = new MockHttpMessageHandler(JsonSerializer.Serialize(mockWorkOsResponse), HttpStatusCode.OK);
        var httpClientFactory = new CustomMockHttpClientFactory(new HttpClient(handler));

        var authService = new GramAuthService(config, httpClientFactory, _storageService, _db);

        var callbackResult = await authService.HandleWorkOsCallbackAsync("code_test_auth_code_123");

        Assert.True(callbackResult.Success);
        Assert.NotNull(callbackResult.User);
        Assert.True(callbackResult.User.IsAuthenticated);
        Assert.Equal("user_01JXYZ987", callbackResult.User.Id);
        Assert.Equal("Alice Lovelace", callbackResult.User.Name);
        Assert.Equal("alice@2helix.org", callbackResult.User.Email);
        Assert.Equal("https://images.example.com/avatar.png", callbackResult.User.Image);
        Assert.Equal("jwt_mock_token_workos_123", callbackResult.Token);

        // Verify active current user in service is updated
        Assert.True(authService.CurrentUser.IsAuthenticated);
        Assert.Equal("Alice Lovelace", authService.CurrentUser.Name);
    }

    private class CustomMockHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public CustomMockHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseContent;
        private readonly HttpStatusCode _statusCode;

        public MockHttpMessageHandler(string responseContent, HttpStatusCode statusCode)
        {
            _responseContent = responseContent;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseContent)
            };
            return Task.FromResult(response);
        }
    }
}
