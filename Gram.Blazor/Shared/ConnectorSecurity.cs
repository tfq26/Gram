using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;

namespace Gram.Blazor.Shared;

public sealed record ConnectorExecutionPolicy(IReadOnlyList<string> AllowedDomains, int TimeoutSeconds = 60, int MemoryLimitMb = 256, int CpuCores = 1, int MaxOutputBytes = 1_000_000);
public sealed record ConnectorCredentialReference(string UserId, string ConnectorId, string ConnectionId, string Name);
public sealed record ConnectorCredentialBinding(string Name, string HeaderName, string Prefix = "");
public sealed record ConnectorEgressRequest(string PackageId, string UserId, string ConnectionId, string Method, string Url, string? Body = null);
public sealed record ConnectorEgressResponse(bool Success, int StatusCode, string Body, string? Error = null);

public sealed class ConnectorEgressClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _token;
    public ConnectorEgressClient()
    {
        _token = Environment.GetEnvironmentVariable("GRAM_CONNECTOR_EGRESS_TOKEN") ?? throw new InvalidOperationException("Egress token is unavailable.");
        var socketPath = Environment.GetEnvironmentVariable("GRAM_CONNECTOR_HOST_SOCKET");
        if (!string.IsNullOrWhiteSpace(socketPath) && !OperatingSystem.IsWindows())
        {
            var handler = new SocketsHttpHandler { ConnectCallback = async (_, ct) => { var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified); await socket.ConnectAsync(new System.Net.Sockets.UnixDomainSocketEndPoint(socketPath), ct); return new System.Net.Sockets.NetworkStream(socket, true); } };
            _http = new HttpClient(handler) { BaseAddress = new Uri("http://connector-host") };
        }
        else _http = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("GRAM_CONNECTOR_EGRESS_URL") ?? "http://127.0.0.1:7340") };
    }
    public async Task<ConnectorEgressResponse> SendAsync(ConnectorEgressRequest request, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/egress") { Content = System.Net.Http.Json.JsonContent.Create(request, options: BackendJson.Options) };
        message.Headers.Add("X-Gram-Execution-Token", _token);
        using var response = await _http.SendAsync(message, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ConnectorEgressResponse>(BackendJson.Options, cancellationToken) ?? new(false, (int)response.StatusCode, "", "Empty egress response.");
    }
    public void Dispose() => _http.Dispose();
}

public interface IConnectorCredentialVault
{
    Task StoreAsync(ConnectorCredentialReference reference, string secret, CancellationToken cancellationToken = default);
    Task<string?> ResolveAsync(ConnectorCredentialReference reference, IReadOnlyList<string> declaredNames, CancellationToken cancellationToken = default);
    Task<bool> DeleteConnectionAsync(string userId, string connectorId, string connectionId, CancellationToken cancellationToken = default);
}

public sealed class ConnectorCredentialVault : IConnectorCredentialVault
{
    private readonly string _path;
    private readonly byte[] _key;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public ConnectorCredentialVault(StorageService storage)
    {
        _path = storage.GetFilePath("connector_credentials.dat");
        _key = LoadKey(storage.GetFilePath("connector_credentials.key"));
    }

    public async Task StoreAsync(ConnectorCredentialReference reference, string secret, CancellationToken cancellationToken = default)
    {
        Validate(reference);
        if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentException("Credential value is required.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var values = await LoadAsync(cancellationToken);
            values[Key(reference)] = secret;
            await WriteAtomicAsync(Encrypt(JsonSerializer.Serialize(values, BackendJson.Options)), cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<string?> ResolveAsync(ConnectorCredentialReference reference, IReadOnlyList<string> declaredNames, CancellationToken cancellationToken = default)
    {
        Validate(reference);
        if (!declaredNames.Contains(reference.Name, StringComparer.OrdinalIgnoreCase)) return null;
        await _gate.WaitAsync(cancellationToken);
        try { var values = await LoadAsync(cancellationToken); return values.GetValueOrDefault(Key(reference)); }
        finally { _gate.Release(); }
    }

    public async Task<bool> DeleteConnectionAsync(string userId, string connectorId, string connectionId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var values = await LoadAsync(cancellationToken);
            var prefix = ScopePrefix(userId, connectorId, connectionId);
            var removed = values.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var key in removed) values.Remove(key);
            if (removed.Count > 0) await WriteAtomicAsync(Encrypt(JsonSerializer.Serialize(values, BackendJson.Options)), cancellationToken);
            return removed.Count > 0;
        }
        finally { _gate.Release(); }
    }

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new(StringComparer.OrdinalIgnoreCase);
        var json = Decrypt(await File.ReadAllBytesAsync(_path, cancellationToken));
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, BackendJson.Options)
            ?? throw new CryptographicException("Connector credential vault contents are invalid.");
    }
    private async Task WriteAtomicAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        var temporaryPath = _path + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
        File.Move(temporaryPath, _path, true);
    }

    private static string Key(ConnectorCredentialReference reference) => string.Join('.',
        EncodeScope(reference.UserId), EncodeScope(reference.ConnectorId), EncodeScope(reference.ConnectionId), EncodeScope(reference.Name));

    private static string ScopePrefix(string userId, string connectorId, string connectionId) => string.Join('.', EncodeScope(userId), EncodeScope(connectorId), EncodeScope(connectionId)) + ".";
    private static string EncodeScope(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static void Validate(ConnectorCredentialReference reference) { if (new[] { reference.UserId, reference.ConnectorId, reference.ConnectionId, reference.Name }.Any(string.IsNullOrWhiteSpace) || reference.UserId.Equals("default", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Credential scope is incomplete or unauthenticated."); }

    private byte[] Encrypt(string value)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(value);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return [1, .. nonce, .. tag, .. ciphertext];
    }

    private string Decrypt(byte[] value)
    {
        if (value.Length < 29 || value[0] != 1) throw new CryptographicException("Connector credential vault format is invalid.");
        var nonce = value[1..13];
        var tag = value[13..29];
        var ciphertext = value[29..];
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] LoadKey(string keyPath)
    {
        var configured = Environment.GetEnvironmentVariable("GRAM_CONNECTOR_VAULT_KEY");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            try
            {
                var key = configured.Length == 64 ? Convert.FromHexString(configured) : Convert.FromBase64String(configured);
                if (key.Length == 32) return key;
            }
            catch { }
            throw new InvalidOperationException("GRAM_CONNECTOR_VAULT_KEY must be a 32-byte base64 or 64-character hex key.");
        }

        if (File.Exists(keyPath))
        {
            var existing = File.ReadAllBytes(keyPath);
            if (existing.Length != 32) throw new CryptographicException("Connector credential vault key is invalid.");
            return existing;
        }

        var generated = RandomNumberGenerator.GetBytes(32);
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
        using (var stream = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(generated);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return generated;
    }
}
