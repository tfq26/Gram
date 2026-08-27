using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public sealed record SearchGroundingRun(
    string Query,
    IReadOnlyList<WebSearchResult> Results,
    string Context,
    int FetchedPages,
    long ElapsedMilliseconds,
    string? Error,
    SearchUsageTelemetry? Usage = null,
    SearchPlan? Plan = null)
{
    public IReadOnlyList<string> MissingCoverage => Plan?.RequiredCoverage?.Where(requirement =>
        !Results.Any(result => ($"{result.Title} {result.Snippet}").Contains(requirement, StringComparison.OrdinalIgnoreCase))).ToList() ?? [];
    public bool IsGrounded => Error is null && Results.Count > 0 && Results.Any(result => !string.IsNullOrWhiteSpace(result.Snippet)) && !string.IsNullOrWhiteSpace(Context) && MissingCoverage.Count == 0;
}

/// <summary>
/// Canonical search-to-grounding pipeline used by diagnostics and the agent loop.
/// It keeps search results and fetched excerpts together so tests exercise the same
/// evidence path the model receives.
/// </summary>
public sealed class SearchGroundingHarness
{
    private readonly WebContentService _webContentService;
    private readonly ILogger<SearchGroundingHarness>? _logger;

    public SearchGroundingHarness(WebContentService webContentService, ILogger<SearchGroundingHarness>? logger = null)
    {
        _webContentService = webContentService;
        _logger = logger;
    }

    public async Task<SearchGroundingRun> RunAsync(string query, int maxResults = 5, CancellationToken cancellationToken = default, bool bypassCache = false)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var plan = SearchPlanBuilder.ForQuestion(query, "gemini-3-flash-preview");
            var results = await _webContentService.SearchWebAsync(plan, maxResults, cancellationToken, bypassCache);
            var context = new StringBuilder();
            var fetchedPages = 0;
            var maxFetchedPages = 1;
            foreach (var result in results)
            {
                var snippet = result.Snippet.Length > 900 ? result.Snippet[..900] + "..." : result.Snippet;
                context.AppendLine($"- {result.Title} — {result.Url}");
                if (!string.IsNullOrWhiteSpace(snippet)) context.AppendLine($"  {snippet}");

                if (fetchedPages < maxFetchedPages && Uri.TryCreate(result.Url, UriKind.Absolute, out _))
                {
                    var page = await _webContentService.ScrapeUrlAsync(result.Url, cancellationToken);
                    if (page.IsSuccess && !string.IsNullOrWhiteSpace(page.ContentMarkdown))
                    {
                        var excerpt = page.ContentMarkdown.Length > 2600 ? page.ContentMarkdown[..2600] + "..." : page.ContentMarkdown;
                        context.AppendLine($"  Page excerpt: {excerpt}");
                        fetchedPages++;
                    }
                }
            }

            stopwatch.Stop();
            var run = new SearchGroundingRun(query, results, context.ToString().Trim(), fetchedPages, stopwatch.ElapsedMilliseconds, null, _webContentService.LastUsage, plan);
            _logger?.LogInformation("Search grounding completed: Grounded={Grounded}, Results={Results}, Pages={Pages}, ElapsedMs={ElapsedMs}, Query={Query}", run.IsGrounded, results.Count, fetchedPages, run.ElapsedMilliseconds, query);
            return run;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(ex, "Search grounding failed for {Query}.", query);
            return new SearchGroundingRun(query, [], "", 0, stopwatch.ElapsedMilliseconds, ex.Message, _webContentService.LastUsage);
        }
    }
}
