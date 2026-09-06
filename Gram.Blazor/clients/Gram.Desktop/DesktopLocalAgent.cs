using System.Net.Http.Json;
using Gram.Blazor.Shared;

namespace Gram.Desktop;

public sealed record DesktopLocalAgentOptions(string Endpoint = "http://127.0.0.1:7340", TimeSpan? Timeout = null);

/// <summary>Readiness boundary for the separate localhost agent; no generic shell/filesystem calls live here.</summary>
public sealed class DesktopLocalAgent
{
    private readonly HttpClient _http;

    public DesktopLocalAgent(DesktopLocalAgentOptions? options = null, HttpMessageHandler? handler = null)
    {
        options ??= new DesktopLocalAgentOptions();
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttp ||
            !(endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || endpoint.Host.Equals("127.0.0.1") || endpoint.Host.Equals("::1")))
            throw new ArgumentException("The local agent endpoint must be an HTTP localhost URL.", nameof(options));

        _http = new HttpClient(handler ?? new SocketsHttpHandler { UseCookies = false })
        {
            BaseAddress = new Uri(endpoint.ToString().TrimEnd('/') + "/"),
            Timeout = options.Timeout ?? TimeSpan.FromSeconds(2)
        };
    }

    public async Task<LocalAgentStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync("local/v1/capabilities", cancellationToken);
            if (!response.IsSuccessStatusCode) return new(false, Capabilities: []);
            return await response.Content.ReadFromJsonAsync<LocalAgentStatus>(BackendJson.Options, cancellationToken) ?? new(false, Capabilities: []);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, Capabilities: []); }
        catch (HttpRequestException) { return new(false, Capabilities: []); }
    }
}
