using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public sealed record WebSearchProviderStatus(string Name, bool IsConfigured);

public interface IWebSearchProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    SearchUsageTelemetry? LastUsage => null;
    Task<IReadOnlyList<WebSearchResult>> SearchAsync(SearchPlan plan, int maxResults, CancellationToken cancellationToken = default)
        => SearchAsync(plan.Queries.FirstOrDefault()?.Query ?? plan.Objective, maxResults, cancellationToken);
    Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// System-key Parallel Search adapter. Fast mode is intentionally used because it is
/// Parallel's recommended balance for interactive agents and its lowest paid search tier.
/// </summary>
public sealed class ParallelWebSearchProvider : IWebSearchProvider
{
    private const string Endpoint = "https://api.parallel.ai/v1/search";
    private readonly HttpClient _httpClient;
    private readonly ILogger<ParallelWebSearchProvider>? _logger;
    private readonly string? _apiKey;
    private readonly IRateLimitCoordinator? _rateLimiter;
    public SearchUsageTelemetry? LastUsage { get; private set; }

    public ParallelWebSearchProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ParallelWebSearchProvider>? logger = null,
        IRateLimitCoordinator? rateLimiter = null)
    {
        _httpClient = httpClientFactory.CreateClient("WebBrowser");
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _apiKey = FirstNonEmpty(configuration["PARALLEL_API_KEY"], configuration["Parallel:ApiKey"]);
        _logger = logger;
        _rateLimiter = rateLimiter;
    }

    public string Name => "Parallel";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
        => await SearchAsync(SearchPlanBuilder.ForQuestion(query), maxResults, cancellationToken);

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        SearchPlan plan,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(plan.Objective) || maxResults <= 0) return [];

        var stopwatch = Stopwatch.StartNew();
        var payload = new JsonObject
        {
            ["objective"] = plan.Objective.Trim(),
            ["search_queries"] = new JsonArray(plan.Queries.Take(3).Select(query => (JsonNode?)JsonValue.Create(BuildConciseQuery(query.Query))).ToArray()),
            ["mode"] = plan.Mode,
            ["client_model"] = plan.ClientModel,
            ["session_id"] = plan.SessionId
        };
        var domains = plan.PreferredDomains?.Where(IsValidDomain).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray() ?? [];
        if (plan.RequiresPrimarySources && domains.Length > 0)
        {
            payload["advanced_settings"] = new JsonObject
            {
                ["source_policy"] = new JsonObject
                {
                    ["include_domains"] = new JsonArray(domains.Select(domain => (JsonNode?)JsonValue.Create(domain)).ToArray())
                },
                ["max_results"] = Math.Clamp(maxResults, 1, 10)
            };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.UserAgent.ParseAdd("Gram-Assistant/1.0");
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var lease = _rateLimiter is null ? null : await _rateLimiter.AcquireAsync(Name, "search", cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        _rateLimiter?.Observe(Name, "search", (int)response.StatusCode, response.Headers.RetryAfter?.Delta is { } delta ? (int)Math.Ceiling(delta.TotalSeconds) : null);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var safeMessage = responseBody.Length > 500 ? responseBody[..500] + "…" : responseBody;
            throw new HttpRequestException($"Parallel returned HTTP {(int)response.StatusCode}: {safeMessage}");
        }

        var document = JsonNode.Parse(responseBody);
        var resultNodes = document?["results"]?.AsArray();
        if (resultNodes is null) return [];

        var providerUnits = document?["usage"]?.AsArray()
            .Where(item => item is not null)
            .Select(item => (Name: item?["name"]?.ToString() ?? "unknown", Count: int.TryParse(item?["count"]?.ToString(), out var count) ? count : 0))
            .Where(item => item.Count > 0)
            .ToDictionary(item => item.Name, item => item.Count, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var results = new List<WebSearchResult>();
        foreach (var node in resultNodes)
        {
            if (results.Count >= maxResults) break;

            var title = node?["title"]?.ToString()?.Trim() ?? "";
            var url = node?["url"]?.ToString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(title) || !IsHttpUrl(url)) continue;
            if (results.Any(result => result.Url.Equals(url, StringComparison.OrdinalIgnoreCase))) continue;

            var excerpts = node?["excerpts"]?.AsArray()
                .Select(excerpt => excerpt?.ToString()?.Trim())
                .Where(excerpt => !string.IsNullOrWhiteSpace(excerpt))
                .Cast<string>()
                .ToList() ?? [];
            var snippet = string.Join("\n\n", excerpts);
            var published = node?["publish_date"]?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(published))
            {
                snippet = string.IsNullOrWhiteSpace(snippet)
                    ? $"Published: {published}"
                    : $"{snippet}\n\nPublished: {published}";
            }

            results.Add(new WebSearchResult(title, url, snippet, Name));
        }

        stopwatch.Stop();
        var searchId = document?["search_id"]?.ToString();
        _logger?.LogInformation(
            "Parallel search completed: {ResultCount} result(s), {ElapsedMs} ms, SearchId={SearchId}",
            results.Count,
            stopwatch.ElapsedMilliseconds,
            searchId);
        var excerptCharacters = results.Sum(result => result.Snippet.Length);
        var additionalResults = providerUnits.Where(item => item.Key.Contains("additional_results", StringComparison.OrdinalIgnoreCase)).Sum(item => item.Value);
        LastUsage = new SearchUsageTelemetry(Name, 1, results.Count, Math.Max(1, plan.Objective.Length / 4), Math.Max(1, excerptCharacters / 4), 0.005m + (additionalResults * 0.001m), true, providerUnits);
        return results;
    }

    private static string BuildConciseQuery(string query)
    {
        var concise = query.Trim().TrimEnd('?', '!', '.');
        concise = System.Text.RegularExpressions.Regex.Replace(
            concise,
            @"^(?:what|who|when|where|why|how)\s+(?:is|are|was|were|did|does|do|will)\s+",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        concise = System.Text.RegularExpressions.Regex.Replace(
            concise,
            @"^(?:the\s+)?(?:plot|story|premise)\s+of\s+",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return string.IsNullOrWhiteSpace(concise) ? query.Trim() : concise;
    }

    private static bool IsHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static bool IsValidDomain(string value) => !string.IsNullOrWhiteSpace(value)
        && !value.Contains('/') && !value.Contains(':') && value.Length <= 253;

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }
}

/// <summary>
/// System-key Tavily adapter. The API key is read only from server configuration;
/// browser requests and user model credentials are never consulted.
/// </summary>
public sealed class TavilyWebSearchProvider : IWebSearchProvider
{
    private const string Endpoint = "https://api.tavily.com/search";
    private readonly HttpClient _httpClient;
    private readonly ILogger<TavilyWebSearchProvider>? _logger;
    private readonly string? _apiKey;
    private readonly IRateLimitCoordinator? _rateLimiter;

    public TavilyWebSearchProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<TavilyWebSearchProvider>? logger = null,
        IRateLimitCoordinator? rateLimiter = null)
    {
        _httpClient = httpClientFactory.CreateClient("WebBrowser");
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _apiKey = FirstNonEmpty(configuration["TAVILY_API_KEY"], configuration["Tavily:ApiKey"]);
        _logger = logger;
        _rateLimiter = rateLimiter;
    }

    public string Name => "Tavily";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
        => await SearchAsync(SearchPlanBuilder.ForQuestion(query), maxResults, cancellationToken);

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        SearchPlan plan,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(plan.Objective) || maxResults <= 0) return [];
        var queries = plan.Queries.Take(3).ToList();
        var all = new List<WebSearchResult>();
        foreach (var queryPlan in queries)
        {
            var results = await SearchSingleAsync(plan, queryPlan, Math.Max(1, maxResults - all.Count), cancellationToken);
            foreach (var result in results.Where(result => all.All(existing => !existing.Url.Equals(result.Url, StringComparison.OrdinalIgnoreCase)))) all.Add(result);
            if (all.Count >= maxResults) break;
        }
        return all.Take(maxResults).ToList();
    }

    private async Task<IReadOnlyList<WebSearchResult>> SearchSingleAsync(SearchPlan plan, SearchQueryPlan queryPlan, int maxResults, CancellationToken cancellationToken)
    {

        var stopwatch = Stopwatch.StartNew();
        var payload = new JsonObject
        {
            ["query"] = queryPlan.Query.Trim(),
            ["topic"] = queryPlan.Topic ?? (plan.Freshness == "current" ? "news" : "general"),
            ["search_depth"] = plan.Mode == "advanced" ? "advanced" : "basic",
            ["max_results"] = Math.Clamp(maxResults, 1, 20),
            ["include_answer"] = false,
            ["include_raw_content"] = false,
            ["include_images"] = false
        };
        var domains = plan.PreferredDomains?.Where(IsValidDomain).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray() ?? [];
        if (plan.RequiresPrimarySources && domains.Length > 0)
        {
            payload["include_domains"] = new JsonArray(domains.Select(domain => (JsonNode?)JsonValue.Create(domain)).ToArray());
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.UserAgent.ParseAdd("Gram-Assistant/1.0");
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var lease = _rateLimiter is null ? null : await _rateLimiter.AcquireAsync(Name, "search", cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        _rateLimiter?.Observe(Name, "search", (int)response.StatusCode, response.Headers.RetryAfter?.Delta is { } delta ? (int)Math.Ceiling(delta.TotalSeconds) : null);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var safeMessage = responseBody.Length > 500 ? responseBody[..500] + "…" : responseBody;
            throw new HttpRequestException($"Tavily returned HTTP {(int)response.StatusCode}: {safeMessage}");
        }

        var document = JsonNode.Parse(responseBody);
        var resultNodes = document?["results"]?.AsArray();
        if (resultNodes is null) return [];

        var results = new List<WebSearchResult>();
        foreach (var node in resultNodes)
        {
            if (results.Count >= maxResults) break;

            var title = node?["title"]?.ToString()?.Trim() ?? "";
            var url = node?["url"]?.ToString()?.Trim() ?? "";
            var content = node?["content"]?.ToString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(title) || !IsHttpUrl(url)) continue;
            if (results.Any(result => result.Url.Equals(url, StringComparison.OrdinalIgnoreCase))) continue;

            results.Add(new WebSearchResult(title, url, content, Name));
        }

        stopwatch.Stop();
        _logger?.LogInformation(
            "Tavily search completed: {ResultCount} result(s), {ElapsedMs} ms",
            results.Count,
            stopwatch.ElapsedMilliseconds);
        return results;
    }

    private static bool IsHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static bool IsValidDomain(string value) => !string.IsNullOrWhiteSpace(value)
        && !value.Contains('/') && !value.Contains(':') && value.Length <= 253;

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }
}
