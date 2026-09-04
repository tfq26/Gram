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
    private readonly SemaphoreSlim _gate = new(1, 1);
    public ConnectorCredentialVault(StorageService storage) => _path = storage.GetFilePath("connector_credentials.dat");

    public async Task StoreAsync(ConnectorCredentialReference reference, string secret, CancellationToken cancellationToken = default)
    {
        Validate(reference);
        if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentException("Credential value is required.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var values = await LoadAsync(cancellationToken);
            values[Key(reference)] = secret;
            await File.WriteAllBytesAsync(_path, Encrypt(JsonSerializer.Serialize(values)), cancellationToken);
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
            var prefix = $"{userId}:{connectorId}:{connectionId}:";
            var removed = values.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var key in removed) values.Remove(key);
            if (removed.Count > 0) await File.WriteAllBytesAsync(_path, Encrypt(JsonSerializer.Serialize(values)), cancellationToken);
            return removed.Count > 0;
        }
        finally { _gate.Release(); }
    }

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new(StringComparer.OrdinalIgnoreCase);
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(Decrypt(await File.ReadAllBytesAsync(_path, cancellationToken))) ?? new(StringComparer.OrdinalIgnoreCase); }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }
    private static string Key(ConnectorCredentialReference reference) => $"{reference.UserId}:{reference.ConnectorId}:{reference.ConnectionId}:{reference.Name}";
    private static void Validate(ConnectorCredentialReference reference) { if (new[] { reference.UserId, reference.ConnectorId, reference.ConnectionId, reference.Name }.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Credential scope is incomplete."); }
    private static byte[] EncryptionKey() => SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + Environment.MachineName + "GramConnectorVault2026"));
    private static byte[] Encrypt(string value) { using var aes = Aes.Create(); aes.Key = EncryptionKey(); aes.GenerateIV(); using var output = new MemoryStream(); output.Write(aes.IV); using (var crypto = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write)) using (var writer = new StreamWriter(crypto)) writer.Write(value); return output.ToArray(); }
    private static string Decrypt(byte[] value) { if (value.Length < 17) return "{}"; using var aes = Aes.Create(); aes.Key = EncryptionKey(); aes.IV = value[..16]; using var input = new MemoryStream(value, 16, value.Length - 16); using var crypto = new CryptoStream(input, aes.CreateDecryptor(), CryptoStreamMode.Read); using var reader = new StreamReader(crypto); return reader.ReadToEnd(); }
}
