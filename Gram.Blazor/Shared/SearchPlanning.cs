namespace Gram.Blazor.Shared;

public sealed record SearchQueryPlan(
    string Query,
    string? Topic = null,
    string? TimeRange = null,
    IReadOnlyList<string>? PreferredDomains = null);

public sealed record SearchPlan(
    string Objective,
    IReadOnlyList<SearchQueryPlan> Queries,
    string Mode = "fast",
    string Freshness = "current",
    IReadOnlyList<string>? PreferredDomains = null,
    bool RequiresPrimarySources = false,
    string? ClientModel = null,
    string? SessionId = null,
    string TopicCategory = "general",
    IReadOnlyList<string>? RequiredCoverage = null);

public enum GroundingRequirement { None, LiveWeb, PrimarySources }

public sealed record GroundingDecision(
    GroundingRequirement Requirement,
    string Freshness,
    string Mode,
    int QueryCount,
    IReadOnlyList<string>? PreferredDomains,
    string TopicCategory)
{
    public bool RequiresSearch => Requirement != GroundingRequirement.None;
    public bool RequiresPrimarySources => Requirement == GroundingRequirement.PrimarySources;
}

/// <summary>Central, subject-independent policy for deciding how Gram grounds answers.</summary>
public static class GroundingPolicy
{
    private sealed record TopicRule(string Category, string[] Signals, string[] Domains);
    private static readonly TopicRule[] TopicRules =
    [
        new("regulation", ["law", "legal", "regulation", "regulatory", "tariff", "tax", "credit", "compliance", "policy", "requirement", "eligibility", "emissions", "passport"], ["irs.gov", "treasury.gov", "ustr.gov", "cbp.gov", "federalregister.gov", "epa.gov", "ec.europa.eu", "eur-lex.europa.eu"]),
        new("finance", ["stock", "share price", "earnings", "revenue", "interest rate", "federal reserve", "inflation", "market cap", "sec filing"], ["federalreserve.gov", "sec.gov", "bea.gov", "bls.gov", "treasury.gov"]),
        new("health", ["medical", "medicine", "clinical", "disease", "drug", "treatment", "symptom", "diagnosis", "health", "fda"], ["fda.gov", "cdc.gov", "nih.gov", "who.int", "clinicaltrials.gov"]),
        new("science", ["nasa", "space", "mission", "launch", "physics", "chemistry", "research", "study", "scientific"], ["nasa.gov", "science.gov", "nih.gov", "noaa.gov", "usgs.gov"]),
        new("software", ["documentation", "docs", "api", "library", "package", "release", "version", "github", "software"], ["learn.microsoft.com", "docs.microsoft.com", "github.com", "developer.mozilla.org", "docs.python.org"]),
        new("sports", ["score", "standings", "schedule", "championship", "league", "game", "match", "formula 1", "nfl", "nba"], ["nfl.com", "nba.com", "mlb.com", "nhl.com", "formula1.com", "uefa.com", "fifa.com"]),
        new("news", ["breaking", "headline", "news", "announcement", "election", "war", "crisis", "what happened"], ["reuters.com", "apnews.com", "bbc.com"])
    ];
    private static readonly string[] LiveSignals = ["latest", "current", "today", "yesterday", "recent", "as of", "news", "price", "release", "announced", "standings", "weather", "what happened", "this year"];
    private static readonly string[] PrimarySignals = ["law", "legal", "regulation", "regulatory", "tariff", "tax", "credit", "compliance", "policy", "official", "requirement", "eligibility", "safety", "medical", "financial"];

    public static GroundingDecision Decide(string question)
    {
        var text = question?.Trim() ?? "";
        var lower = text.ToLowerInvariant();
        var needsPrimary = PrimarySignals.Any(lower.Contains);
        var needsLive = needsPrimary || LiveSignals.Any(lower.Contains);
        var complex = text.Length > 180 || lower.Contains("compare") || lower.Contains(" and ") || lower.Contains("versus") || lower.Contains(" vs ");
        var rule = TopicRules
            .Select(candidate => (Rule: candidate, Matches: candidate.Signals.Count(signal => lower.Contains(signal, StringComparison.OrdinalIgnoreCase))))
            .Where(candidate => candidate.Matches > 0)
            .OrderByDescending(candidate => candidate.Matches)
            .FirstOrDefault();
        var category = rule.Rule?.Category ?? "general";
        var domains = needsPrimary && rule.Rule is not null ? rule.Rule.Domains : null;
        return new(
            needsPrimary ? GroundingRequirement.PrimarySources : needsLive ? GroundingRequirement.LiveWeb : GroundingRequirement.None,
            needsLive ? "current" : "general",
            complex ? "advanced" : "fast",
            complex ? 3 : 1,
            domains,
            category);
    }

    public static bool RequiresWebSearch(string? question) => !string.IsNullOrWhiteSpace(question) && Decide(question).RequiresSearch;
}

public static class SearchPlanBuilder
{
    public static SearchPlan ForQuestion(string question, string? clientModel = null)
    {
        var text = question.Trim();
        var decision = GroundingPolicy.Decide(text);
        var coverage = BuildCoverage(text, decision.TopicCategory);
        var current = decision.Freshness == "current";
        var primary = decision.RequiresPrimarySources;
        var mode = decision.Mode;
        var concise = System.Text.RegularExpressions.Regex.Replace(text, @"^(what|who|when|where|why|how)\s+(is|are|was|were|did|does|do|will)\s+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).TrimEnd('?', '.', '!');
        var primaryQuery = string.IsNullOrWhiteSpace(concise) ? text : concise;
        var queries = new List<SearchQueryPlan> { new(primaryQuery, current ? "news" : "general") };
        if (mode == "advanced")
        {
            var terms = System.Text.RegularExpressions.Regex.Matches(primaryQuery, @"[A-Za-z0-9][A-Za-z0-9'\-.]{2,}")
                .Select(match => match.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
            if (terms.Count >= 5)
            {
                var midpoint = Math.Max(3, terms.Count / 2);
                queries.Add(new SearchQueryPlan(string.Join(' ', terms.Take(midpoint)), current ? "news" : "general"));
                queries.Add(new SearchQueryPlan(string.Join(' ', terms.Skip(midpoint).Take(5)), current ? "news" : "general"));
            }
        }
        IReadOnlyList<string>? domains = decision.PreferredDomains;
        return new SearchPlan(text, queries, mode, current ? "current" : "general", domains, primary, clientModel, $"gram-{Guid.NewGuid():N}", decision.TopicCategory, coverage);
    }

    private static IReadOnlyList<string> BuildCoverage(string text, string category)
    {
        var lower = text.ToLowerInvariant();
        var coverage = new List<string>();
        if (lower.Contains("compare") || lower.Contains("versus") || lower.Contains(" vs "))
        {
            if (lower.Contains("u.s.") || lower.Contains("us ") || lower.Contains("united states")) coverage.Add("United States");
            if (lower.Contains("eu") || lower.Contains("european union") || lower.Contains("europe")) coverage.Add("European Union");
        }
        if (category == "regulation")
        {
            foreach (var focus in new[] { "tariff", "tax credit", "battery", "emissions", "regulation", "eligibility" })
                if (lower.Contains(focus, StringComparison.OrdinalIgnoreCase)) coverage.Add(focus);
        }
        if (category == "finance") foreach (var focus in new[] { "price", "earnings", "revenue", "interest rate", "outlook" }) if (lower.Contains(focus)) coverage.Add(focus);
        if (category == "health") foreach (var focus in new[] { "safety", "trial", "treatment", "side effects", "guidance" }) if (lower.Contains(focus)) coverage.Add(focus);
        return coverage.Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList();
    }
}

public static class SourceQualityScorer
{
    private static readonly string[] PrimaryHosts = ["irs.gov", "treasury.gov", "ustr.gov", "epa.gov", "ec.europa.eu", "eur-lex.europa.eu", "nasa.gov", "federalreserve.gov", "sec.gov", "who.int", "un.org"];

    public static int Score(WebSearchResult result, SearchPlan? plan = null)
    {
        if (!Uri.TryCreate(result.Url, UriKind.Absolute, out var uri)) return 0;
        var score = 0;
        if (PrimaryHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase))) score += 100;
        else if (uri.Host.EndsWith(".gov", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".europa.eu", StringComparison.OrdinalIgnoreCase)) score += 80;
        else if (result.Source?.Equals("Parallel", StringComparison.OrdinalIgnoreCase) == true || result.Source?.Equals("Tavily", StringComparison.OrdinalIgnoreCase) == true) score += 30;
        if (result.Snippet.Contains("published:", StringComparison.OrdinalIgnoreCase)) score += 5;
        if (plan?.RequiresPrimarySources == true && score < 80) score -= 20;
        return score;
    }

    public static IReadOnlyList<WebSearchResult> Rank(IEnumerable<WebSearchResult> results, SearchPlan? plan = null) =>
        plan?.RequiresPrimarySources == true
            ? results.OrderByDescending(result => Score(result, plan)).ThenByDescending(result => result.Snippet.Length).ToList()
            : results.ToList();
}
