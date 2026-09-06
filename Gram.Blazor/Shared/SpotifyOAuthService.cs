using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Gram.Blazor.Shared;

public sealed record SpotifyAuthorizationStart(string AuthorizationUrl, string State);

public sealed class SpotifyOAuthService : IConnectorConnectionRefresher
{
    private readonly IConnectorCredentialVault _vault;
    private readonly IConnectorConnectionStore _connections;
    private readonly ConcurrentDictionary<string, PendingState> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshLocks = new(StringComparer.Ordinal);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    public SpotifyOAuthService(IConnectorCredentialVault vault, IConnectorConnectionStore connections, HttpClient? http = null) { _vault = vault; _connections = connections; _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) }; }
    public string ConnectorId => "spotify";

    public async Task<bool> TryRefreshAsync(string userId, string connectionId, CancellationToken cancellationToken = default)
    {
        await RefreshAsync(userId, connectionId, cancellationToken);
        return true;
    }

    public SpotifyAuthorizationStart Start(string userId, string? returnUrl = null)
    {
        var clientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID");
        var redirect = Environment.GetEnvironmentVariable("SPOTIFY_REDIRECT_URI") ?? "http://127.0.0.1:7337/api/v1/connectors/spotify/callback";
        if (string.IsNullOrWhiteSpace(clientId)) throw new InvalidOperationException("SPOTIFY_CLIENT_ID is not configured.");
        if (!IsAllowedRedirect(redirect)) throw new InvalidOperationException("SPOTIFY_REDIRECT_URI must use HTTPS or an explicit loopback IP (127.0.0.1 or [::1]); localhost is not accepted by Spotify.");
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var sha = SHA256.Create();
        var challenge = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _pending[state] = new PendingState(userId, verifier, redirect, returnUrl, DateTimeOffset.UtcNow.AddMinutes(10));
        var scopes = string.Join(' ', new[] { "user-read-currently-playing", "user-read-playback-state", "user-modify-playback-state" });
        var query = new Dictionary<string, string> { ["response_type"] = "code", ["client_id"] = clientId, ["scope"] = scopes, ["redirect_uri"] = redirect, ["state"] = state, ["code_challenge_method"] = "S256", ["code_challenge"] = challenge };
        return new SpotifyAuthorizationStart("https://accounts.spotify.com/authorize?" + string.Join('&', query.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}")), state);
    }

    public async Task<(ConnectorConnection Connection, string? ReturnUrl)> CompleteAsync(string code, string state, CancellationToken cancellationToken = default)
    {
        if (!_pending.TryRemove(state, out var pending) || pending.ExpiresAt <= DateTimeOffset.UtcNow) throw new InvalidOperationException("Spotify authorization state is invalid or expired.");
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = pending.RedirectUri, ["client_id"] = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID") ?? "", ["code_verifier"] = pending.Verifier }) };
        using var tokenResponse = await _http.SendAsync(tokenRequest, cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<SpotifyToken>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Spotify returned an empty token response.");
        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me");
        profileRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var profileResponse = await _http.SendAsync(profileRequest, cancellationToken);
        profileResponse.EnsureSuccessStatusCode();
        var profile = await profileResponse.Content.ReadFromJsonAsync<SpotifyProfile>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Spotify account verification failed.");
        var connectionId = Guid.NewGuid().ToString("N");
        var scopes = (token.Scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        await _vault.StoreAsync(new ConnectorCredentialReference(pending.UserId, "spotify", connectionId, "refresh_token"), token.RefreshToken ?? "", cancellationToken);
        await _vault.StoreAsync(new ConnectorCredentialReference(pending.UserId, "spotify", connectionId, "access_token"), token.AccessToken, cancellationToken);
        var connection = new ConnectorConnection(connectionId, "spotify", profile.DisplayName ?? profile.Id ?? "Spotify account", scopes, true, DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, token.ExpiresIn - 60)), pending.UserId, "oauth-pkce", DateTimeOffset.UtcNow, Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID"));
        await _connections.SaveAsync(connection, cancellationToken);
        return (connection, pending.ReturnUrl);
    }

    public async Task<ConnectorConnection> RefreshAsync(string userId, string connectionId, CancellationToken cancellationToken = default)
    {
        var lockKey = $"{userId}\u001f{connectionId}";
        var refreshLock = _refreshLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            var connection = (await _connections.ListAsync(userId, "spotify", cancellationToken)).FirstOrDefault(x => x.Id.Equals(connectionId, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException("Spotify connection was not found.");
            var refresh = await _vault.ResolveAsync(new ConnectorCredentialReference(userId, "spotify", connectionId, "refresh_token"), ["refresh_token"], cancellationToken) ?? throw new InvalidOperationException("Spotify refresh token is unavailable.");
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh, ["client_id"] = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID") ?? "" }) };
            using var response = await _http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<SpotifyToken>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Spotify returned an empty refresh response.");
            await _vault.StoreAsync(new ConnectorCredentialReference(userId, "spotify", connectionId, "access_token"), token.AccessToken, cancellationToken);
            if (!string.IsNullOrWhiteSpace(token.RefreshToken))
                await _vault.StoreAsync(new ConnectorCredentialReference(userId, "spotify", connectionId, "refresh_token"), token.RefreshToken, cancellationToken);
            var updated = connection with { IsConnected = true, ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, token.ExpiresIn - 60)), LastVerifiedAt = DateTimeOffset.UtcNow, Scopes = string.IsNullOrWhiteSpace(token.Scope) ? connection.Scopes : token.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries) };
            await _connections.SaveAsync(updated, cancellationToken);
            return updated;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public async Task<bool> DisconnectAsync(string userId, string connectionId, CancellationToken cancellationToken = default)
    {
        await _vault.DeleteConnectionAsync(userId, "spotify", connectionId, cancellationToken);
        return await _connections.DeleteAsync(userId, "spotify", connectionId, cancellationToken);
    }

    private sealed record PendingState(string UserId, string Verifier, string RedirectUri, string? ReturnUrl, DateTimeOffset ExpiresAt);
    private sealed record SpotifyToken([property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken, [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn, [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string? RefreshToken, string? Scope);
    private sealed record SpotifyProfile([property: System.Text.Json.Serialization.JsonPropertyName("display_name")] string? DisplayName, string? Id);
    private static bool IsAllowedRedirect(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Host is "127.0.0.1" or "[::1]" or "::1")
        && !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
}
