using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Gram.Blazor.Shared;

/// REST/SSE client shared by desktop and mobile shells. It deliberately depends only
/// on the public contracts, so either client can be moved to another process later.
public sealed class GramBackendClient
{
    private readonly HttpClient _http;
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

    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<T>(path, BackendJson.Options, ct) ?? throw new InvalidOperationException("Backend returned an empty response.");

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
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/runs/{Uri.EscapeDataString(runId)}/events");
        if (afterSequence > 0) request.Headers.TryAddWithoutValidation("Last-Event-ID", afterSequence.ToString());
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

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    { await EnsureSuccessAsync(response, ct); return await response.Content.ReadFromJsonAsync<T>(BackendJson.Options, ct) ?? throw new InvalidOperationException("Backend returned an empty response."); }
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    { if (response.IsSuccessStatusCode) return; var error = await response.Content.ReadAsStringAsync(ct); throw new HttpRequestException($"Backend request failed ({(int)response.StatusCode}): {error}"); }
}
