using System.Net;
using System.Net.Http.Json;
using Gram.Blazor.Shared;
using GramBox.Contracts;

namespace Gram.Desktop;

public sealed record DesktopLocalAgentOptions(
    string Endpoint = "http://127.0.0.1:7340",
    string? Secret = null,
    TimeSpan? Timeout = null
);

public interface ILocalAgentClient
{
    bool IsAvailable { get; }
    bool IsUnauthorized { get; }
    Task<LocalAgentStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<FilePickResult> PickFileAsync(FilePickRequest request, CancellationToken cancellationToken = default);
    Task<ExecutionResult> RunTerminalAsync(TerminalRunRequest request, CancellationToken cancellationToken = default);
    Task<LocalSandboxCreateResult> CreateSandboxAsync(LocalSandboxCreateRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Authenticated client boundary for the separate localhost agent.
/// Requires an installation pairing secret and exposes named capabilities only.
/// </summary>
public sealed class DesktopLocalAgent : ILocalAgentClient
{
    private readonly HttpClient _http;

    public bool IsAvailable { get; private set; }
    public bool IsUnauthorized { get; private set; }

    public DesktopLocalAgent(DesktopLocalAgentOptions? options = null, HttpMessageHandler? handler = null)
    {
        options ??= new DesktopLocalAgentOptions();
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttp ||
            !(endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || endpoint.Host.Equals("127.0.0.1") || endpoint.Host.Equals("::1")))
            throw new ArgumentException("The local agent endpoint must be an HTTP localhost URL.", nameof(options));

        _http = new HttpClient(handler ?? new SocketsHttpHandler { UseCookies = false })
        {
            BaseAddress = new Uri(endpoint.ToString().TrimEnd('/') + "/"),
            Timeout = options.Timeout ?? TimeSpan.FromSeconds(5)
        };

        var secret = options.Secret ?? LoadInstallationSecret();
        if (!string.IsNullOrWhiteSpace(secret))
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Local-Agent-Secret", secret);
        }
    }

    public async Task<LocalAgentStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync("local/v1/capabilities", cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                IsUnauthorized = true;
                IsAvailable = false;
                return new(false, Capabilities: []);
            }

            if (!response.IsSuccessStatusCode)
            {
                IsAvailable = false;
                return new(false, Capabilities: []);
            }

            var status = await response.Content.ReadFromJsonAsync<LocalAgentStatus>(BackendJson.Options, cancellationToken) 
                ?? new(false, Capabilities: []);
            IsAvailable = status.IsAvailable;
            IsUnauthorized = false;
            return status;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            IsAvailable = false;
            return new(false, Capabilities: []);
        }
        catch (HttpRequestException)
        {
            IsAvailable = false;
            return new(false, Capabilities: []);
        }
    }

    public async Task<FilePickResult> PickFileAsync(FilePickRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync("local/v1/files/pick", request, BackendJson.Options, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                IsUnauthorized = true;
                return new FilePickResult(false, null, null, 0, "Local agent unauthorized: pairing secret required.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                return new FilePickResult(false, null, null, 0, $"Local agent error (HTTP {(int)response.StatusCode}): {err}");
            }

            return await response.Content.ReadFromJsonAsync<FilePickResult>(BackendJson.Options, cancellationToken)
                ?? new FilePickResult(false, null, null, 0, "Empty response from local agent.");
        }
        catch (Exception ex)
        {
            return new FilePickResult(false, null, null, 0, ex.Message);
        }
    }

    public async Task<ExecutionResult> RunTerminalAsync(TerminalRunRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync("local/v1/terminal/run", request, BackendJson.Options, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                IsUnauthorized = true;
                return new ExecutionResult(false, 401, "", "Local agent unauthorized: pairing secret required.", 0, "UNAUTHORIZED");
            }

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                return new ExecutionResult(false, (int)response.StatusCode, "", err, 0, $"Local agent error: {err}");
            }

            return await response.Content.ReadFromJsonAsync<ExecutionResult>(BackendJson.Options, cancellationToken)
                ?? new ExecutionResult(false, -1, "", "Empty response from local agent.", 0, "EMPTY_RESPONSE");
        }
        catch (Exception ex)
        {
            return new ExecutionResult(false, -1, "", ex.Message, 0, ex.Message);
        }
    }

    public async Task<LocalSandboxCreateResult> CreateSandboxAsync(LocalSandboxCreateRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync("local/v1/sandbox/create", request, BackendJson.Options, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                IsUnauthorized = true;
                return new LocalSandboxCreateResult(false, "", "", "Local agent unauthorized: pairing secret required.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                return new LocalSandboxCreateResult(false, "", "", $"Local agent error (HTTP {(int)response.StatusCode}): {err}");
            }

            return await response.Content.ReadFromJsonAsync<LocalSandboxCreateResult>(BackendJson.Options, cancellationToken)
                ?? new LocalSandboxCreateResult(false, "", "", "Empty response from local agent.");
        }
        catch (Exception ex)
        {
            return new LocalSandboxCreateResult(false, "", "", ex.Message);
        }
    }

    private static string? LoadInstallationSecret()
    {
        var envSecret = Environment.GetEnvironmentVariable("GRAM_LOCAL_AGENT_SECRET");
        if (!string.IsNullOrWhiteSpace(envSecret)) return envSecret.Trim();

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        var secretPath = Path.Combine(localAppData, "Gram", "local-agent.secret");
        if (File.Exists(secretPath))
        {
            var text = File.ReadAllText(secretPath).Trim();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return null;
    }
}
