using System.Text;

namespace Gram.Blazor.Shared;

public sealed record ResearchRequest(string Question, int MaxSources = 8, IReadOnlyList<string>? PreferredDomains = null, bool RequirePrimarySources = false);
public sealed record ResearchEvidenceSource(string Id, string Title, string Url, string Provider, int QualityScore, bool FullPageFetched, string Evidence);
public sealed record ResearchContradiction(string Topic, string SourceAId, string SourceBId, string Description);
public sealed record ResearchEvidencePacket(
    string Question, 
    IReadOnlyList<ResearchEvidenceSource> Sources, 
    IReadOnlyList<string> MissingCoverage, 
    string ModelContext, 
    bool IsReady, 
    long ElapsedMilliseconds, 
    SearchUsageTelemetry? Usage = null,
    IReadOnlyList<ResearchContradiction>? Contradictions = null);

public sealed class ResearchEvidenceService
{
    private readonly WebContentService _web;
    public ResearchEvidenceService(WebContentService web) => _web = web;

    public async Task<ResearchEvidencePacket> BuildAsync(ResearchRequest request, CancellationToken cancellationToken = default)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var basePlan = SearchPlanBuilder.ForQuestion(request.Question, "gram-research-engine");
        var plan = basePlan with
        {
            PreferredDomains = request.PreferredDomains?.Count > 0 ? request.PreferredDomains : basePlan.PreferredDomains,
            RequiresPrimarySources = request.RequirePrimarySources || basePlan.RequiresPrimarySources,
            Mode = request.Question.Length > 180 || basePlan.Mode == "advanced" ? "advanced" : basePlan.Mode
        };
        var raw = await _web.SearchWebAsync(plan, Math.Clamp(request.MaxSources, 2, 12), cancellationToken, bypassCache: false);
        var ranked = SourceQualityScorer.Rank(raw, plan).Take(Math.Clamp(request.MaxSources, 2, 12)).ToList();
        var fetchLimit = plan.Mode == "advanced" ? 4 : 2;
        var sources = new List<ResearchEvidenceSource>();

        for (var index = 0; index < ranked.Count; index++)
        {
            var result = ranked[index];
            var evidence = result.Snippet;
            var fetched = false;
            if (index < fetchLimit)
            {
                var page = await _web.ScrapeUrlAsync(result.Url, cancellationToken, plan.PreferredDomains);
                if (page.IsSuccess && !string.IsNullOrWhiteSpace(page.ContentMarkdown))
                {
                    evidence = page.ContentMarkdown.Length > 5000 ? page.ContentMarkdown[..5000] + "…" : page.ContentMarkdown;
                    fetched = true;
                }
            }
            sources.Add(new ResearchEvidenceSource($"s{index + 1}", result.Title, result.Url, result.Source ?? "web", SourceQualityScorer.Score(result, plan), fetched, evidence));
        }

        var coverage = plan.RequiredCoverage ?? [];
        var missing = coverage.Where(requirement => !sources.Any(source => $"{source.Title} {source.Evidence}".Contains(requirement, StringComparison.OrdinalIgnoreCase))).ToList();
        var contradictions = DetectContradictions(sources);
        var context = BuildContext(request.Question, sources, missing, contradictions);
        started.Stop();
        var ready = sources.Count >= 2 && sources.Any(source => source.FullPageFetched) && (!plan.RequiresPrimarySources || sources.Any(source => source.QualityScore >= 80)) && missing.Count == 0;
        return new ResearchEvidencePacket(request.Question, sources, missing, context, ready, started.ElapsedMilliseconds, _web.LastUsage, contradictions);
    }

    public static List<ResearchContradiction> DetectContradictions(IReadOnlyList<ResearchEvidenceSource> sources)
    {
        var contradictions = new List<ResearchContradiction>();
        if (sources.Count < 2) return contradictions;

        // Metric contradiction pattern: look for currency, percentages, or discrete metric figures
        var metricRegex = new System.Text.RegularExpressions.Regex(@"(\$\s?\d+[\d,\.]*\s*(?:billion|million|trillion|k|m|b)?|\b\d+(?:\.\d+)?%\b)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        for (var i = 0; i < sources.Count; i++)
        {
            for (var j = i + 1; j < sources.Count; j++)
            {
                var srcA = sources[i];
                var srcB = sources[j];

                // If both sources share keywords from a similar topic but quote conflicting metrics
                var tokensA = srcA.Evidence.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(w => w.Length > 4).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var commonTokens = srcB.Evidence.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(w => w.Length > 4 && tokensA.Contains(w)).ToList();

                if (commonTokens.Count >= 3)
                {
                    var matchesA = metricRegex.Matches(srcA.Evidence).Select(m => m.Value.ToLowerInvariant()).Distinct().ToList();
                    var matchesB = metricRegex.Matches(srcB.Evidence).Select(m => m.Value.ToLowerInvariant()).Distinct().ToList();

                    if (matchesA.Count > 0 && matchesB.Count > 0 && !matchesA.Intersect(matchesB).Any())
                    {
                        var desc = $"Potential contradiction on '{string.Join(", ", commonTokens.Take(3))}': {srcA.Id} mentions [{string.Join(", ", matchesA.Take(2))}] while {srcB.Id} mentions [{string.Join(", ", matchesB.Take(2))}].";
                        contradictions.Add(new ResearchContradiction(string.Join(" ", commonTokens.Take(3)), srcA.Id, srcB.Id, desc));
                    }
                }
            }
        }

        return contradictions;
    }

    private static string BuildContext(string question, IReadOnlyList<ResearchEvidenceSource> sources, IReadOnlyList<string> missing, IReadOnlyList<ResearchContradiction> contradictions)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<research_packet>");
        builder.AppendLine($"<question>{System.Net.WebUtility.HtmlEncode(question)}</question>");
        foreach (var source in sources)
        {
            builder.AppendLine($"<source id=\"{source.Id}\" quality=\"{source.QualityScore}\" fetched=\"{source.FullPageFetched.ToString().ToLowerInvariant()}\">");
            builder.AppendLine($"<title>{System.Net.WebUtility.HtmlEncode(source.Title)}</title>");
            builder.AppendLine($"<url>{System.Net.WebUtility.HtmlEncode(source.Url)}</url>");
            builder.AppendLine($"<evidence>{System.Net.WebUtility.HtmlEncode(source.Evidence)}</evidence>");
            builder.AppendLine("</source>");
        }
        if (missing.Count > 0) builder.AppendLine($"<gaps>{System.Net.WebUtility.HtmlEncode(string.Join("; ", missing))}</gaps>");
        if (contradictions.Count > 0)
        {
            builder.AppendLine("<contradictions>");
            foreach (var c in contradictions)
            {
                builder.AppendLine($"  <contradiction sources=\"{c.SourceAId}, {c.SourceBId}\" topic=\"{System.Net.WebUtility.HtmlEncode(c.Topic)}\">{System.Net.WebUtility.HtmlEncode(c.Description)}</contradiction>");
            }
            builder.AppendLine("</contradictions>");
        }
        builder.AppendLine("Use only supported claims. Cite source URLs and state uncertainty for uncovered requirements or conflicting evidence.");
        builder.Append("</research_packet>");
        return builder.ToString();
    }
}
