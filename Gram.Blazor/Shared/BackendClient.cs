using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Gram.Blazor.Shared;

/// REST/SSE client shared by desktop and mobile shells. It deliberately depends only
/// on the public contracts, so either client can be moved to another process later.
public sealed class GramBackendClient : IMemoryApi
{
    private readonly HttpClient _http;
    private const int MaxReadAttempts = 3;
    public GramBackendClient(HttpClient http, string? accessToken = null)
    {
        _http = http;
        SetAccessToken(accessToken);
    }

    public void SetAccessToken(string? accessToken)
    {
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(accessToken)
            ? null
            : new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public Task<T> GetAsync<T>(string path, CancellationToken ct = default) =>
        SendReadAsync<T>(path, ct);

    public Task<IReadOnlyList<ConnectorDescriptor>> GetConnectorsAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<ConnectorDescriptor>>("/api/v1/connectors", ct);
    public Task<IReadOnlyList<ConnectorConnection>> GetConnectorConnectionsAsync(string? connectorId = null, CancellationToken ct = default) => GetAsync<IReadOnlyList<ConnectorConnection>>($"/api/v1/connectors/connections{(connectorId == null ? "" : "?connectorId=" + Uri.EscapeDataString(connectorId))}", ct);

    public Task<IReadOnlyList<StructuredMemory>> GetMemoriesAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<StructuredMemory>>("/api/v1/memory", ct);

    public Task<IReadOnlyList<StructuredMemory>> SearchMemoriesAsync(string query, string? projectId = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<StructuredMemory>>($"/api/v1/memory/search?q={Uri.EscapeDataString(query)}{(string.IsNullOrWhiteSpace(projectId) ? "" : "&projectId=" + Uri.EscapeDataString(projectId))}", ct);

    public Task<StructuredMemory> RememberAsync(RememberMemoryRequest request, CancellationToken ct = default) =>
        PostAsync<StructuredMemory>("/api/v1/memory", request, ct);

    Task<IReadOnlyList<StructuredMemory>> IMemoryApi.ListAsync(CancellationToken ct) => GetMemoriesAsync(ct);
    Task<IReadOnlyList<StructuredMemory>> IMemoryApi.SearchAsync(string query, string? projectId, CancellationToken ct) => SearchMemoriesAsync(query, projectId, ct);

    async Task<bool> IMemoryApi.ConfirmAsync(string id, CancellationToken ct) { await ConfirmMemoryAsync(id, ct); return true; }
    async Task<bool> IMemoryApi.RejectAsync(string id, CancellationToken ct) { await RejectMemoryAsync(id, ct); return true; }
    async Task<bool> IMemoryApi.ForgetAsync(string id, CancellationToken ct) { await ForgetMemoryAsync(id, ct); return true; }

    public async Task ConfirmMemoryAsync(string memoryId, CancellationToken ct = default) =>
        await PostAsync<object>($"/api/v1/memory/{Uri.EscapeDataString(memoryId)}/confirm", new { }, ct);

    public async Task RejectMemoryAsync(string memoryId, CancellationToken ct = default) =>
        await PostAsync<object>($"/api/v1/memory/{Uri.EscapeDataString(memoryId)}/reject", new { }, ct);

    public Task ForgetMemoryAsync(string memoryId, CancellationToken ct = default) =>
        DeleteAsync($"/api/v1/memory/{Uri.EscapeDataString(memoryId)}", ct);

    public async Task<T> PostAsync<T>(string path, object body, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync(path, body, BackendJson.Options, ct);
        return await ReadAsync<T>(response, ct);
    }

    public async Task<T> PutAsync<T>(string path, object body, CancellationToken ct = default)
    {
        using var response = await _http.PutAsJsonAsync(path, body, BackendJson.Options, ct);
        return await ReadAsync<T>(response, ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync(path, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async IAsyncEnumerable<RunEvent> SubscribeToRunAsync(string runId, long afterSequence = 0, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in ReadEventConnectionAsync(runId, afterSequence, ct))
        {
            if (item.Sequence > afterSequence) yield return item;
        }
    }

    private async IAsyncEnumerable<RunEvent> ReadEventConnectionAsync(string runId, long lastSequence, [EnumeratorCancellation] CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/runs/{Uri.EscapeDataString(runId)}/events");
        if (lastSequence > 0) request.Headers.TryAddWithoutValidation("Last-Event-ID", lastSequence.ToString());
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        string? data = null;
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (line.StartsWith("data: ", StringComparison.Ordinal)) data = line[6..];
            else if (line.Length == 0 && data is not null)
            {
                var item = JsonSerializer.Deserialize<RunEvent>(data, BackendJson.Options);
                if (item is not null) yield return item;
                data = null;
            }
        }
    }

    private async Task<T> SendReadAsync<T>(string path, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, path))
            using (var response = await _http.SendAsync(request, ct))
            {
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadFromJsonAsync<T>(BackendJson.Options, ct) ?? throw new InvalidOperationException("Backend returned an empty response.");
                if (!IsTransient(response.StatusCode) || attempt >= MaxReadAttempts - 1)
                    await EnsureSuccessAsync(response, ct);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)), ct);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) => statusCode == HttpStatusCode.RequestTimeout || statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    { await EnsureSuccessAsync(response, ct); return await response.Content.ReadFromJsonAsync<T>(BackendJson.Options, ct) ?? throw new InvalidOperationException("Backend returned an empty response."); }
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    { if (response.IsSuccessStatusCode) return; var error = await response.Content.ReadAsStringAsync(ct); throw new HttpRequestException($"Backend request failed ({(int)response.StatusCode}): {error}"); }
}
