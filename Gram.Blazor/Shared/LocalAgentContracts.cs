namespace Gram.Blazor.Shared;

public sealed record LocalAgentHello(string ClientId, string InstallationId, string PairingSecret);
public sealed record LocalAgentCapabilityRequest(string RunId, string Capability, string? SandboxId = null, string? Path = null, string? Command = null);
public sealed record LocalFilePickResult(string? Path, bool Cancelled);
public sealed record LocalTerminalResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);
public sealed record LocalSandboxResult(string SandboxId, string RootPath, string Status);
public sealed record LocalPerformanceSession(string SessionId, DateTimeOffset StartedAt);

public interface ILocalCapabilityClient
{
    Task<LocalFilePickResult> PickFileAsync(LocalAgentCapabilityRequest request, CancellationToken cancellationToken = default);
    Task<LocalTerminalResult> RunTerminalAsync(LocalAgentCapabilityRequest request, CancellationToken cancellationToken = default);
    Task<LocalSandboxResult> CreateSandboxAsync(LocalAgentCapabilityRequest request, CancellationToken cancellationToken = default);
    Task<LocalAgentStatus> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
}

/// Client for the paired localhost agent. There is intentionally no generic
/// filesystem or shell method; every operation maps to a named capability.
public sealed class LocalAgentHttpClient : ILocalCapabilityClient
{
    private readonly HttpClient _http;
    public LocalAgentHttpClient(HttpClient http) => _http = http;
    public Task<LocalFilePickResult> PickFileAsync(LocalAgentCapabilityRequest request, CancellationToken cancellationToken = default) => PostAsync<LocalFilePickResult>("/local/v1/files/pick", request, cancellationToken);
    public Task<LocalTerminalResult> RunTerminalAsync(LocalAgentCapabilityRequest request, CancellationToken cancellationToken = default) => PostAsync<LocalTerminalResult>("/local/v1/terminal/run", request, cancellationToken);
    public Task<LocalSandboxResult> CreateSandboxAsync(LocalAgentCapabilityRequest request, CancellationToken cancellationToken = default) => PostAsync<LocalSandboxResult>("/local/v1/sandboxes", request, cancellationToken);
    public async Task<LocalAgentStatus> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => await _http.GetFromJsonAsync<LocalAgentStatus>("/local/v1/capabilities", BackendJson.Options, cancellationToken) ?? new LocalAgentStatus(false);
    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    { using var response = await _http.PostAsJsonAsync(path, body, BackendJson.Options, ct); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<T>(BackendJson.Options, ct) ?? throw new InvalidOperationException("Local agent returned an empty response."); }
}
