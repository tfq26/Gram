using System.Net;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public sealed record WebSearchResult(string Title, string Url, string Snippet, string? Source = null);
public sealed record SearchUsageTelemetry(
    string Provider,
    int Requests,
    int Results,
    int EstimatedInputTokens,
    int EstimatedOutputTokens,
    decimal? EstimatedCostUsd,
    bool CostIsEstimate,
    IReadOnlyDictionary<string, int>? ProviderUnits = null);

public sealed record WebScrapeResult(
    string Url, 
    string Title, 
    string ContentMarkdown, 
    bool IsSuccess, 
    string? ErrorMessage = null,
    string? Description = null,
    string? Author = null);

public sealed class WebContentService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebContentService>? _logger;
    private readonly IReadOnlyList<IWebSearchProvider> _searchProviders;
    private readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAt, List<WebSearchResult> Results)> _searchCache = new();
    public SearchUsageTelemetry? LastUsage { get; private set; }

    public WebContentService(
        IHttpClientFactory httpClientFactory,
        ILogger<WebContentService>? logger = null,
        IEnumerable<IWebSearchProvider>? searchProviders = null)
    {
        _httpClient = httpClientFactory.CreateClient("WebBrowser");
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _logger = logger;
        _searchProviders = searchProviders?.ToList() ?? [];
    }

    public IReadOnlyList<WebSearchProviderStatus> ProviderStatuses => _searchProviders
        .Select(provider => new WebSearchProviderStatus(provider.Name, provider.IsConfigured))
        .ToList();

    /// <summary>
    /// Extracts URLs from any text string.
    /// </summary>
    public static List<string> ExtractUrls(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var matches = Regex.Matches(text, @"https?://[^\s<>""'#]+[^\s<>""'#.,;!?]", RegexOptions.IgnoreCase);
        var urls = new List<string>();
        foreach (Match m in matches)
        {
            if (Uri.TryCreate(m.Value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                if (!urls.Contains(m.Value)) urls.Add(m.Value);
            }
        }
        return urls;
    }

    /// <summary>
    /// Fetches a URL and transforms its HTML into clean, human-readable Markdown.
    /// Strips scripts, ads, headers, navbars, tracking widgets, and footers.
    /// </summary>
    public async Task<WebScrapeResult> ScrapeUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return new WebScrapeResult(url, "", "", false, "Invalid URL format.");
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            req.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,text/plain;q=0.8,*/*;q=0.7");
            req.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                return new WebScrapeResult(url, "", "", false, $"HTTP {(int)res.StatusCode}: {res.ReasonPhrase}");
            }

            var contentType = res.Content.Headers.ContentType?.MediaType ?? "";
            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) || 
                contentType.Contains("text/plain", StringComparison.OrdinalIgnoreCase))
            {
                var plain = await res.Content.ReadAsStringAsync(cancellationToken);
                return new WebScrapeResult(url, uri.Host, plain.Length > 15000 ? plain[..15000] + "\n\n...[content truncated]" : plain, true);
            }

            var html = await res.Content.ReadAsStringAsync(cancellationToken);
            return ParseHtmlToMarkdown(url, html);
        }
        catch (Exception ex)
        {
            return new WebScrapeResult(url, "", "", false, ex.Message);
        }
    }

    /// <summary>
    /// Performs resilient multi-source search using configured API providers first,
    /// followed by DuckDuckGo, Google News, and Wikipedia fallbacks.
    /// </summary>
    public async Task<List<WebSearchResult>> SearchWebAsync(string query, int maxResults = 5, CancellationToken cancellationToken = default, bool bypassCache = false)
        => await SearchWebAsync(SearchPlanBuilder.ForQuestion(query), maxResults, cancellationToken, bypassCache);

    public async Task<List<WebSearchResult>> SearchWebAsync(SearchPlan plan, int maxResults = 5, CancellationToken cancellationToken = default, bool bypassCache = false)
    {
        var query = plan.Objective;
        if (string.IsNullOrWhiteSpace(query)) return [];
        LastUsage = null;
        var stopwatch = Stopwatch.StartNew();
        var cacheKey = $"{query.Trim().ToLowerInvariant()}::{maxResults}";
        if (!bypassCache && _searchCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            LastUsage = null;
            _logger?.LogInformation("Web search cache hit: {ResultCount} result(s) in {ElapsedMs} ms", cached.Results.Count, stopwatch.ElapsedMilliseconds);
            return cached.Results.Select(result => result with {}).ToList();
        }

        var results = new List<WebSearchResult>();

        // 1. Configured API providers. Parallel is registered first, followed by Tavily.
        foreach (var provider in _searchProviders.Where(provider => provider.IsConfigured))
        {
            try
            {
                var providerResults = await provider.SearchAsync(plan, maxResults - results.Count, cancellationToken);
                LastUsage = provider.LastUsage;
                foreach (var result in providerResults)
                {
                    if (results.Count >= maxResults) break;
                    if (results.Any(existing => existing.Url.Equals(result.Url, StringComparison.OrdinalIgnoreCase))) continue;
                    results.Add(result);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "{ProviderName} search failed; continuing with fallback sources", provider.Name);
            }

            if (results.Count >= maxResults) break;
        }

        // 2. DuckDuckGo Instant Answers API fallback
        if (results.Count < maxResults)
        {
        try
        {
            var instantUrl = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1&skip_disambig=1";
            using var req = new HttpRequestMessage(HttpMethod.Get, instantUrl);
            req.Headers.UserAgent.ParseAdd("Gram-Assistant/1.0");

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync(cancellationToken);
                var doc = JsonNode.Parse(json);
                if (doc != null)
                {
                    var abstractText = doc["AbstractText"]?.ToString();
                    var abstractUrl = doc["AbstractURL"]?.ToString();
                    var heading = doc["Heading"]?.ToString();

                    if (!string.IsNullOrWhiteSpace(abstractText) && !string.IsNullOrWhiteSpace(abstractUrl))
                    {
                        results.Add(new WebSearchResult(
                            string.IsNullOrWhiteSpace(heading) ? query : heading,
                            abstractUrl,
                            abstractText,
                            "DuckDuckGo Instant"
                        ));
                    }

                    var related = doc["RelatedTopics"]?.AsArray();
                    if (related != null)
                    {
                        foreach (var topic in related)
                        {
                            if (results.Count >= maxResults) break;
                            var text = topic?["Text"]?.ToString();
                            var firstUrl = topic?["FirstURL"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(firstUrl))
                            {
                                results.Add(new WebSearchResult(
                                    text.Length > 60 ? text[..60] + "..." : text,
                                    firstUrl,
                                    text,
                                    "DuckDuckGo"
                                ));
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "DuckDuckGo Instant search failed");
        }
        }

        // 3. DuckDuckGo HTML Search Scraper fallback
        if (results.Count < maxResults)
        {
            try
            {
                var searchUrl = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";
                using var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

                using var res = await _httpClient.SendAsync(req, cancellationToken);
                if (res.IsSuccessStatusCode)
                {
                    var html = await res.Content.ReadAsStringAsync(cancellationToken);

                    var resultBlocks = Regex.Matches(html, @"<div class=""[^""]*result results_links[^""]*""[^>]*>(.*?)<\/div>\s*<\/div>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    if (resultBlocks.Count == 0)
                    {
                        resultBlocks = Regex.Matches(html, @"<div class=""result__body"">(.*?)<\/div>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    }

                    foreach (Match block in resultBlocks)
                    {
                        if (results.Count >= maxResults) break;
                        var blockHtml = block.Groups[1].Value;

                        var titleMatch = Regex.Match(blockHtml, @"<a class=""result__a""[^>]*href=""([^""]+)""[^>]*>(.*?)<\/a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                        if (!titleMatch.Success) continue;

                        var rawUrl = titleMatch.Groups[1].Value;
                        var rawTitle = titleMatch.Groups[2].Value;
                        var cleanTitle = Regex.Replace(WebUtility.HtmlDecode(rawTitle), "<.*?>", string.Empty).Trim();
                        var cleanUrl = CleanSearchUrl(rawUrl);

                        var snippetMatch = Regex.Match(blockHtml, @"<a class=""result__snippet""[^>]*>(.*?)<\/a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                        var snippet = snippetMatch.Success 
                            ? Regex.Replace(WebUtility.HtmlDecode(snippetMatch.Groups[1].Value), "<.*?>", string.Empty).Trim()
                            : "";

                        if (!string.IsNullOrWhiteSpace(cleanUrl) && !results.Any(r => r.Url == cleanUrl))
                        {
                            if (string.IsNullOrWhiteSpace(cleanTitle))
                            {
                                try { cleanTitle = new Uri(cleanUrl).Host; } catch { cleanTitle = "Web Result"; }
                            }

                            results.Add(new WebSearchResult(
                                cleanTitle,
                                cleanUrl,
                                snippet,
                                "Web"
                            ));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "DuckDuckGo HTML search failed");
            }
        }

        // 4. Google News RSS fallback for current-event and entertainment queries.
        // Reserve one slot for an encyclopedia result so news headlines cannot crowd out
        // a directly fetchable background source (for example, a movie's Wikipedia page).
        if (results.Count < maxResults)
        {
            try
            {
                var googleNewsLimit = Math.Max(1, maxResults - 1);
                var newsUrl = $"https://news.google.com/rss/search?q={Uri.EscapeDataString(query)}&hl=en-US&gl=US&ceid=US:en";
                using var req = new HttpRequestMessage(HttpMethod.Get, newsUrl);
                req.Headers.UserAgent.ParseAdd("Gram-Assistant/1.0");

                using var res = await _httpClient.SendAsync(req, cancellationToken);
                if (res.IsSuccessStatusCode)
                {
                    var xml = await res.Content.ReadAsStringAsync(cancellationToken);
                    var document = XDocument.Parse(xml);
                    foreach (var item in document.Descendants("item"))
                    {
                        if (results.Count >= googleNewsLimit) break;

                        var title = WebUtility.HtmlDecode(item.Element("title")?.Value ?? "").Trim();
                        var link = item.Element("link")?.Value?.Trim() ?? "";
                        var descriptionHtml = item.Element("description")?.Value ?? "";
                        var description = Regex.Replace(WebUtility.HtmlDecode(descriptionHtml), "<.*?>", string.Empty).Trim();
                        var sourceName = item.Element("source")?.Value?.Trim();
                        var published = item.Element("pubDate")?.Value?.Trim();

                        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link)) continue;
                        if (results.Any(result => result.Url == link || result.Title.Equals(title, StringComparison.OrdinalIgnoreCase))) continue;

                        var snippet = description;
                        if (!string.IsNullOrWhiteSpace(published)) snippet = $"{snippet} Published: {published}".Trim();
                        results.Add(new WebSearchResult(title, link, snippet, string.IsNullOrWhiteSpace(sourceName) ? "Google News" : $"Google News · {sourceName}"));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Google News RSS search failed");
            }
        }

        // 5. Wikipedia API fallback
        if (results.Count < maxResults)
        {
            try
            {
                var encyclopediaQuery = NormalizeEncyclopediaQuery(query);
                var wikiUrl = $"https://en.wikipedia.org/w/api.php?action=opensearch&search={Uri.EscapeDataString(encyclopediaQuery)}&limit={maxResults}&namespace=0&format=json";
                using var req = new HttpRequestMessage(HttpMethod.Get, wikiUrl);
                req.Headers.UserAgent.ParseAdd("Gram-Assistant/1.0");

                using var res = await _httpClient.SendAsync(req, cancellationToken);
                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadAsStringAsync(cancellationToken);
                    var arr = JsonNode.Parse(json)?.AsArray();
                    if (arr != null && arr.Count >= 4)
                    {
                        var titles = arr[1]?.AsArray();
                        var descriptions = arr[2]?.AsArray();
                        var links = arr[3]?.AsArray();

                        if (titles != null && descriptions != null && links != null)
                        {
                            for (int i = 0; i < titles.Count && results.Count < maxResults; i++)
                            {
                                var t = titles[i]?.ToString() ?? "";
                                var desc = descriptions[i]?.ToString() ?? "";
                                var link = links[i]?.ToString() ?? "";
                                if (!string.IsNullOrWhiteSpace(t) && !string.IsNullOrWhiteSpace(link))
                                {
                                    if (results.Any(result => result.Url == link)) continue;

                                    // Put the directly fetchable encyclopedia page ahead of news
                                    // redirect URLs. AgentOrchestrator expands the first two results.
                                    results.Insert(0, new WebSearchResult(t, link, desc, "Wikipedia"));
                                    if (results.Count > maxResults) results.RemoveAt(results.Count - 1);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Wikipedia search failed");
            }
        }

        results = SourceQualityScorer.Rank(results, plan).Take(maxResults).ToList();
        _searchCache[cacheKey] = (DateTimeOffset.UtcNow.Add(results.Count > 0 ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(30)), results.ToList());
        stopwatch.Stop();
        _logger?.LogInformation(
            "Web search completed: {ResultCount} result(s), {SourceCount} source type(s), {ElapsedMs} ms",
            results.Count,
            results.Select(result => result.Source ?? "Unknown").Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            stopwatch.ElapsedMilliseconds);

        return results;
    }

    private static string CleanSearchUrl(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return "";
        rawUrl = WebUtility.HtmlDecode(rawUrl).Trim();
        if (rawUrl.StartsWith("//")) rawUrl = "https:" + rawUrl;

        if (rawUrl.Contains("uddg="))
        {
            var match = Regex.Match(rawUrl, @"[?&]uddg=([^&]+)");
            if (match.Success)
            {
                var decoded = Uri.UnescapeDataString(match.Groups[1].Value);
                if (Uri.TryCreate(decoded, UriKind.Absolute, out var cleanUri) && 
                    (cleanUri.Scheme == Uri.UriSchemeHttp || cleanUri.Scheme == Uri.UriSchemeHttps))
                {
                    return cleanUri.ToString();
                }
            }
        }

        if (rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return rawUrl;
        }

        return rawUrl;
    }

    private static string NormalizeEncyclopediaQuery(string query)
    {
        var normalized = query.Trim().TrimEnd('?', '!', '.');
        normalized = Regex.Replace(
            normalized,
            @"^(?:what|who|when|where|why|how)\s+(?:is|are|was|were|did|does|do|will)\s+",
            "",
            RegexOptions.IgnoreCase);
        normalized = Regex.Replace(
            normalized,
            @"^(?:the\s+)?(?:plot|story|premise)\s+of\s+",
            "",
            RegexOptions.IgnoreCase);

        return string.IsNullOrWhiteSpace(normalized) ? query : normalized.Trim();
    }

    private static WebScrapeResult ParseHtmlToMarkdown(string url, string html)
    {
        try
        {
            var titleMatch = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var title = titleMatch.Success ? WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim() : "";

            var descMatch = Regex.Match(html, @"<meta\s+name=[""']description[""']\s+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (!descMatch.Success)
            {
                descMatch = Regex.Match(html, @"<meta\s+property=[""']og:description[""']\s+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            }
            var description = descMatch.Success ? WebUtility.HtmlDecode(descMatch.Groups[1].Value).Trim() : null;

            var clean = Regex.Replace(html, @"<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            clean = Regex.Replace(clean, @"<style\b[^<]*(?:(?!<\/style>)<[^<]*)*<\/style>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            clean = Regex.Replace(clean, @"<svg\b[^<]*(?:(?!<\/svg>)<[^<]*)*<\/svg>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            clean = Regex.Replace(clean, @"<nav\b[^<]*(?:(?!<\/nav>)<[^<]*)*<\/nav>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            clean = Regex.Replace(clean, @"<header\b[^<]*(?:(?!<\/header>)<[^<]*)*<\/header>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            clean = Regex.Replace(clean, @"<footer\b[^<]*(?:(?!<\/footer>)<[^<]*)*<\/footer>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            clean = Regex.Replace(clean, @"<noscript\b[^<]*(?:(?!<\/noscript>)<[^<]*)*<\/noscript>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            clean = Regex.Replace(clean, @"<!--.*?-->", " ", RegexOptions.Singleline);

            var mainMatch = Regex.Match(clean, @"<main\b[^>]*>(.*?)</main>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (mainMatch.Success)
            {
                clean = mainMatch.Groups[1].Value;
            }
            else
            {
                var articleMatch = Regex.Match(clean, @"<article\b[^>]*>(.*?)</article>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (articleMatch.Success)
                {
                    clean = articleMatch.Groups[1].Value;
                }
            }

            clean = Regex.Replace(clean, @"<h1[^>]*>(.*?)</h1>", "\n\n# $1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            clean = Regex.Replace(clean, @"<h2[^>]*>(.*?)</h2>", "\n\n## $1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            clean = Regex.Replace(clean, @"<h3[^>]*>(.*?)</h3>", "\n\n### $1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            clean = Regex.Replace(clean, @"<h[4-6][^>]*>(.*?)</h[4-6]>", "\n\n#### $1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            clean = Regex.Replace(clean, @"<p[^>]*>(.*?)</p>", "\n\n$1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            clean = Regex.Replace(clean, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

            clean = Regex.Replace(clean, @"<li[^>]*>(.*?)</li>", "\n- $1", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            clean = Regex.Replace(clean, @"</?[uo]l[^>]*>", "\n", RegexOptions.IgnoreCase);

            clean = Regex.Replace(clean, @"<a\s+[^>]*href=[""']([^""']+)[""'][^>]*>(.*?)</a>", "[$2]($1)", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            clean = Regex.Replace(clean, @"<(?:strong|b)[^>]*>(.*?)</(?:strong|b)>", "**$1**", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            clean = Regex.Replace(clean, @"<(?:em|i)[^>]*>(.*?)</(?:em|i)>", "*$1*", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            clean = Regex.Replace(clean, @"<pre[^>]*><code[^>]*>(.*?)</code></pre>", "\n```\n$1\n```\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            clean = Regex.Replace(clean, @"<code[^>]*>(.*?)</code>", "`$1`", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            clean = Regex.Replace(clean, @"<[^>]+>", " ");
            clean = WebUtility.HtmlDecode(clean);

            clean = Regex.Replace(clean, @"[ \t]+", " ");
            clean = Regex.Replace(clean, @"\n{3,}", "\n\n");
            clean = clean.Trim();

            if (clean.Length > 15000)
            {
                clean = clean[..15000] + "\n\n...[content truncated]...";
            }

            var md = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(title))
            {
                md.AppendLine($"# {title}\n");
            }
            if (!string.IsNullOrWhiteSpace(description))
            {
                md.AppendLine($"> {description}\n");
            }
            md.AppendLine(clean);

            return new WebScrapeResult(url, title, md.ToString(), true, null, description);
        }
        catch (Exception ex)
        {
            return new WebScrapeResult(url, "", "", false, $"HTML Parser Error: {ex.Message}");
        }
    }
}
