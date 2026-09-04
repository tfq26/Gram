using System.Net.Sockets;
using System.Net.Http.Json;

namespace Gram.Blazor.Shared;

public interface IConnectorHostClient
{
    Task<ConnectorActionResult> ExecuteAsync(ConnectorHostExecuteRequest request, CancellationToken cancellationToken = default);
}

public sealed class ConnectorHostClient : IConnectorHostClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly string? _secret;

    public ConnectorHostClient()
    {
        _secret = Environment.GetEnvironmentVariable("GRAM_CONNECTOR_HOST_SECRET");
        var socketPath = Environment.GetEnvironmentVariable("GRAM_CONNECTOR_HOST_SOCKET");
        if (!string.IsNullOrWhiteSpace(socketPath) && !OperatingSystem.IsWindows())
        {
            var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (_, ct) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try { await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct); return new NetworkStream(socket, ownsSocket: true); }
                    catch { socket.Dispose(); throw; }
                }
            };
            _http = new HttpClient(handler) { BaseAddress = new Uri("http://connector-host"), Timeout = TimeSpan.FromSeconds(65) };
            return;
        }

        var url = Environment.GetEnvironmentVariable("GRAM_CONNECTOR_HOST_URL") ?? "http://127.0.0.1:7340";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !(uri.Host is "127.0.0.1" or "localhost" or "::1"))
            throw new InvalidOperationException("Connector host TCP transport must use a loopback address. Hosted transport requires a separate authenticated client.");
        _http = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(65) };
    }

    public async Task<ConnectorActionResult> ExecuteAsync(ConnectorHostExecuteRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_secret)) return new(false, "isolated_host_unconfigured", "Connector host authentication is not configured.");
        using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/execute") { Content = JsonContent.Create(request, options: BackendJson.Options) };
        message.Headers.Add("X-Gram-Connector-Secret", _secret);
        try
        {
            using var response = await _http.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode) return new(false, "isolated_host_error", $"Connector host returned HTTP {(int)response.StatusCode}.");
            return await response.Content.ReadFromJsonAsync<ConnectorActionResult>(BackendJson.Options, cancellationToken) ?? new(false, "isolated_host_error", "Connector host returned an empty response.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new(false, "isolated_host_unavailable", ex.Message); }
    }

    public void Dispose() => _http.Dispose();
}
