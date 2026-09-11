using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using GramBox.Contracts;

namespace GramBox.Client;

public interface IGramBoxClient
{
    Task<SandboxCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
    Task<SandboxJobSnapshot> SubmitJobAsync(SandboxJobRequest request, CancellationToken cancellationToken = default);
    Task<SandboxJobSnapshot> GetJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task<bool> CancelJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task<SandboxInfo> CreateAsync(CreateSandboxRequest request, CancellationToken cancellationToken = default);
    Task<ExecutionResult> ExecuteAsync(string sandboxId, ExecuteCommandRequest request, CancellationToken cancellationToken = default);
    Task<bool> WriteFileAsync(string sandboxId, FileWriteRequest request, CancellationToken cancellationToken = default);
    Task<FileReadResult> ReadFileAsync(string sandboxId, string relativePath, CancellationToken cancellationToken = default);
    Task<DiffSummary> GetDiffAsync(string sandboxId, CancellationToken cancellationToken = default);
    Task<bool> CloseAsync(string sandboxId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<SandboxEvent> SubscribeAsync(string sandboxId, long afterSequence = 0, CancellationToken cancellationToken = default);
}

/// REST client for GramBox. Consumers depend on this interface and the contracts
/// assembly; runtime drivers and host-specific implementation details stay private.
public sealed class GramBoxClient : IGramBoxClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public GramBoxClient(HttpClient http) => _http = http;

    public Task<SandboxCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => GetAsync<SandboxCapabilities>("/api/v1/sandboxes/capabilities", cancellationToken);
    public Task<SandboxJobSnapshot> SubmitJobAsync(SandboxJobRequest request, CancellationToken cancellationToken = default) => PostAsync<SandboxJobSnapshot>("/api/v1/sandbox-jobs", request, cancellationToken);
    public Task<SandboxJobSnapshot> GetJobAsync(string jobId, CancellationToken cancellationToken = default) => GetAsync<SandboxJobSnapshot>($"/api/v1/sandbox-jobs/{Escape(jobId)}", cancellationToken);
    public async Task<bool> CancelJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsync($"/api/v1/sandbox-jobs/{Escape(jobId)}/cancel", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return true;
    }
    public Task<SandboxInfo> CreateAsync(CreateSandboxRequest request, CancellationToken cancellationToken = default) => PostAsync<SandboxInfo>("/api/v1/sandboxes", request, cancellationToken);
    public Task<ExecutionResult> ExecuteAsync(string sandboxId, ExecuteCommandRequest request, CancellationToken cancellationToken = default) => PostAsync<ExecutionResult>($"/api/v1/sandboxes/{Escape(sandboxId)}/commands", request, cancellationToken);
    public Task<bool> WriteFileAsync(string sandboxId, FileWriteRequest request, CancellationToken cancellationToken = default) => PostAsync<bool>($"/api/v1/sandboxes/{Escape(sandboxId)}/files", request, cancellationToken);
    public Task<FileReadResult> ReadFileAsync(string sandboxId, string relativePath, CancellationToken cancellationToken = default) => GetAsync<FileReadResult>($"/api/v1/sandboxes/{Escape(sandboxId)}/files?path={Uri.EscapeDataString(relativePath)}", cancellationToken);
    public Task<DiffSummary> GetDiffAsync(string sandboxId, CancellationToken cancellationToken = default) => GetAsync<DiffSummary>($"/api/v1/sandboxes/{Escape(sandboxId)}/diff", cancellationToken);
    public async Task<bool> CloseAsync(string sandboxId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync($"/api/v1/sandboxes/{Escape(sandboxId)}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return true;
    }

    public async IAsyncEnumerable<SandboxEvent> SubscribeAsync(string sandboxId, long afterSequence = 0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/sandboxes/{Escape(sandboxId)}/events");
        if (afterSequence > 0) request.Headers.TryAddWithoutValidation("Last-Event-ID", afterSequence.ToString());
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        string? data = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) yield break;
            if (line.StartsWith("data: ", StringComparison.Ordinal)) data = line[6..];
            else if (line.Length == 0 && data is not null)
            {
                var item = JsonSerializer.Deserialize<SandboxEvent>(data, Json);
                if (item is not null && item.Sequence > afterSequence) { afterSequence = item.Sequence; yield return item; }
                data = null;
            }
        }
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw new InvalidOperationException("GramBox returned an empty response.");
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(path, body, Json, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw new InvalidOperationException("GramBox returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new GramBoxClientException(response.StatusCode, body);
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
}

public sealed class GramBoxClientException(HttpStatusCode statusCode, string responseBody) : HttpRequestException($"GramBox request failed ({(int)statusCode}): {responseBody}")
{
    public string ResponseBody { get; } = responseBody;
}
