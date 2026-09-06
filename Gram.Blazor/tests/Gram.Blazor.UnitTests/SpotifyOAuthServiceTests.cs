using System.Net;
using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class SpotifyOAuthServiceTests
{
    [Fact]
    public void StartUsesExplicitLoopbackRedirectAndPkceState()
    {
        var oldClientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID");
        var oldRedirect = Environment.GetEnvironmentVariable("SPOTIFY_REDIRECT_URI");
        try
        {
            Environment.SetEnvironmentVariable("SPOTIFY_CLIENT_ID", "client-test");
            Environment.SetEnvironmentVariable("SPOTIFY_REDIRECT_URI", "http://127.0.0.1:7337/api/v1/connectors/spotify/callback");
            var service = new SpotifyOAuthService(new FakeVault(), new FakeStore());

            var result = service.Start("user-1", "/settings");

            Assert.False(string.IsNullOrWhiteSpace(result.State));
            Assert.Contains("client-test", result.AuthorizationUrl);
            Assert.Contains("127.0.0.1%3A7337", result.AuthorizationUrl);
            Assert.Contains("code_challenge_method=S256", result.AuthorizationUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPOTIFY_CLIENT_ID", oldClientId);
            Environment.SetEnvironmentVariable("SPOTIFY_REDIRECT_URI", oldRedirect);
        }
    }

    [Fact]
    public void StartRejectsLocalhostRedirect()
    {
        var oldClientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID");
        var oldRedirect = Environment.GetEnvironmentVariable("SPOTIFY_REDIRECT_URI");
        try
        {
            Environment.SetEnvironmentVariable("SPOTIFY_CLIENT_ID", "client-test");
            Environment.SetEnvironmentVariable("SPOTIFY_REDIRECT_URI", "http://localhost:7337/api/v1/connectors/spotify/callback");
            var service = new SpotifyOAuthService(new FakeVault(), new FakeStore());

            var error = Assert.Throws<InvalidOperationException>(() => service.Start("user-1"));

            Assert.Contains("localhost", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPOTIFY_CLIENT_ID", oldClientId);
            Environment.SetEnvironmentVariable("SPOTIFY_REDIRECT_URI", oldRedirect);
        }
    }

    [Fact]
    public async Task RefreshPersistsRotatedRefreshToken()
    {
        var oldClientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID");
        try
        {
            Environment.SetEnvironmentVariable("SPOTIFY_CLIENT_ID", "client-test");
            var vault = new FakeVault { Values = { ["refresh_token"] = "old-refresh" } };
            var store = new FakeStore(new ConnectorConnection("c1", "spotify", "Account", [], true, DateTimeOffset.UtcNow.AddMinutes(-1), "user-1"));
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"new-access\",\"expires_in\":3600,\"refresh_token\":\"new-refresh\"}")
            });
            var service = new SpotifyOAuthService(vault, store, new HttpClient(handler));

            await service.RefreshAsync("user-1", "c1");

            Assert.Equal("new-access", vault.Values["access_token"]);
            Assert.Equal("new-refresh", vault.Values["refresh_token"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPOTIFY_CLIENT_ID", oldClientId);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(callback(request));
    }

    private sealed class FakeVault : IConnectorCredentialVault
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Task StoreAsync(ConnectorCredentialReference reference, string secret, CancellationToken cancellationToken = default) { Values[reference.Name] = secret; return Task.CompletedTask; }
        public Task<string?> ResolveAsync(ConnectorCredentialReference reference, IReadOnlyList<string> declaredNames, CancellationToken cancellationToken = default) => Task.FromResult(Values.GetValueOrDefault(reference.Name));
        public Task<bool> DeleteConnectionAsync(string userId, string connectorId, string connectionId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class FakeStore(ConnectorConnection? initial = null) : IConnectorConnectionStore
    {
        private ConnectorConnection? _connection = initial;
        public Task<IReadOnlyList<ConnectorConnection>> ListAsync(string userId, string? connectorId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ConnectorConnection>>(_connection == null ? [] : [_connection]);
        public Task<ConnectorConnection> SaveAsync(ConnectorConnection value, CancellationToken cancellationToken = default) { _connection = value; return Task.FromResult(value); }
        public Task<bool> DeleteAsync(string userId, string connectorId, string connectionId, CancellationToken cancellationToken = default) { _connection = null; return Task.FromResult(true); }
    }
}
