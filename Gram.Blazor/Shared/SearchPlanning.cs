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

    private static bool HasSignal(string text, string signal)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(signal)) return false;
        if (signal.Contains(' ')) return text.Contains(signal, StringComparison.OrdinalIgnoreCase);
        return System.Text.RegularExpressions.Regex.IsMatch(text, $@"\b{System.Text.RegularExpressions.Regex.Escape(signal)}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    public static GroundingDecision Decide(string question)
    {
        var text = question?.Trim() ?? "";
        var lower = text.ToLowerInvariant();
        var needsPrimary = PrimarySignals.Any(s => HasSignal(lower, s));
        var needsLive = needsPrimary || LiveSignals.Any(s => HasSignal(lower, s));
        var complex = text.Length > 180 || lower.Contains("compare") || lower.Contains(" and ") || lower.Contains("versus") || lower.Contains(" vs ");
        var rule = TopicRules
            .Select(candidate => (Rule: candidate, Matches: candidate.Signals.Count(signal => HasSignal(lower, signal))))
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
    private static readonly string[] ForwardLookingEntities = ["apple", "google", "microsoft", "openai", "meta", "nvidia", "amd", "intel", "tesla", "samsung", "iphone", "mac", "ipad", "android", "gemini", "chatgpt", "claude", "hardware", "phone", "chip"];

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
        var lower = text.ToLowerInvariant();

        var queries = new List<SearchQueryPlan> { new(primaryQuery, current ? "news" : "general") };

        // Only inject hardware/tech roadmap queries if the query explicitly targets consumer tech / hardware entities
        var isHardwareOrConsumerTech = ForwardLookingEntities.Any(e =>
            System.Text.RegularExpressions.Regex.IsMatch(lower, $@"\b{System.Text.RegularExpressions.Regex.Escape(e)}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

        if (isHardwareOrConsumerTech)
        {
            queries.Add(new SearchQueryPlan($"{primaryQuery} official event announcement date news", "news"));
            queries.Add(new SearchQueryPlan($"{primaryQuery} upcoming hardware roadmap rumors expected release timeline", "news"));
        }
        else
        {
            // Category-relevant query planning without spurious hardware/rumor injection
            switch (decision.TopicCategory)
            {
                case "regulation":
                    queries.Add(new SearchQueryPlan($"{primaryQuery} official regulation rule guidance compliance federal register", "general"));
                    break;
                case "finance":
                    queries.Add(new SearchQueryPlan($"{primaryQuery} official earnings report financial filing statement", current ? "news" : "general"));
                    break;
                case "health":
                    queries.Add(new SearchQueryPlan($"{primaryQuery} official medical guidance clinical trial safety fda cdc", "general"));
                    break;
                case "science":
                    queries.Add(new SearchQueryPlan($"{primaryQuery} official research paper scientific study mission data", "general"));
                    break;
                case "sports":
                    queries.Add(new SearchQueryPlan($"{primaryQuery} official score standings schedule results", current ? "news" : "general"));
                    break;
                case "news":
                    queries.Add(new SearchQueryPlan($"{primaryQuery} official announcement breaking news details", "news"));
                    break;
                default:
                    if (current)
                    {
                        queries.Add(new SearchQueryPlan($"{primaryQuery} latest updates official announcement", "news"));
                    }
                    break;
            }
        }

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
    private static readonly string[] Tier1AOfficialHosts =
    [
        "wikipedia.org", "apple.com", "microsoft.com", "google.com", "openai.com", "anthropic.com", 
        "sec.gov", "irs.gov", "treasury.gov", "ustr.gov", "epa.gov", "ec.europa.eu", 
        "eur-lex.europa.eu", "nasa.gov", "federalreserve.gov", "who.int", "reuters.com", "apnews.com"
    ];

    private static readonly string[] Tier1BReportingHosts =
    [
        "bloomberg.com", "theverge.com", "9to5mac.com", "macrumors.com", "arstechnica.com", 
        "wsj.com", "ft.com", "cnbc.com", "techcrunch.com", "wired.com", "engadget.com", 
        "tomshardware.com", "anandtech.com", "bbc.com", "nytimes.com"
    ];

    private static readonly string[] SpamSignals = ["coupon", "promo code", "discount code", "cheap deals", "affiliate", "best deals today", "free gift card"];

    public static int Score(WebSearchResult result, SearchPlan? plan = null)
    {
        if (!Uri.TryCreate(result.Url, UriKind.Absolute, out var uri)) return 0;
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var score = 0;

        // Tier 1A: Official primary sources and encyclopedias
        if (Tier1AOfficialHosts.Any(h => host.Equals(h, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase)))
        {
            score += 100;
        }
        // Tier 1B: Vetted journalism with strong track record
        else if (Tier1BReportingHosts.Any(h => host.Equals(h, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase)))
        {
            score += 80;
        }
        else if (host.EndsWith(".gov", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".europa.eu", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".edu", StringComparison.OrdinalIgnoreCase))
        {
            score += 85;
        }
        else
        {
            score += 40;
        }

        // Boost specific news / encyclopedia article permalinks over top-level homepages
        if (uri.AbsolutePath.Length > 1 && (uri.AbsolutePath.Contains("/wiki/") || uri.AbsolutePath.Contains("/202") || uri.AbsolutePath.Contains("/news/") || uri.AbsolutePath.Contains("/articles/") || uri.AbsolutePath.Contains("/story/")))
        {
            score += 25;
        }

        // Penalize stale historical years for current / live news queries
        if (plan?.Freshness == "current")
        {
            if (uri.AbsolutePath.Contains("/2018/") || uri.AbsolutePath.Contains("/2019/") || 
                uri.AbsolutePath.Contains("/2020/") || uri.AbsolutePath.Contains("/2021/") || 
                uri.AbsolutePath.Contains("/2022/") || uri.AbsolutePath.Contains("/2023/") ||
                uri.AbsolutePath.Contains("/2018-") || uri.AbsolutePath.Contains("/2019-") || 
                uri.AbsolutePath.Contains("/2020-") || uri.AbsolutePath.Contains("/2021-"))
            {
                score -= 120;
            }
        }

        // Adjust news redirect aggregators so direct publisher/encyclopedia articles rank higher
        if (host.Equals("news.google.com", StringComparison.OrdinalIgnoreCase))
        {
            score -= 10;
        }

        // Penalize spam / affiliate aggregators
        var text = $"{result.Title} {result.Snippet}".ToLowerInvariant();
        if (SpamSignals.Any(text.Contains))
        {
            score -= 60;
        }

        if (result.Snippet.Contains("published:", StringComparison.OrdinalIgnoreCase)) score += 10;
        if (plan?.RequiresPrimarySources == true && score < 80) score -= 30;

        return score;
    }

    public static IReadOnlyList<WebSearchResult> Rank(IEnumerable<WebSearchResult> results, SearchPlan? plan = null)
    {
        var scored = results
            .Where(r => plan?.PreferredDomains == null || plan.PreferredDomains.Count == 0 || UrlNormalization.IsDomainAllowed(r.Url, plan.PreferredDomains))
            .Select((r, i) => (Result: r, Score: Score(r, plan), Index: i, CanonicalUrl: UrlNormalization.CanonicalizeUrl(r.Url)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .ToList();

        // 1. Deduplicate by canonical URL
        var uniqueByUrl = new List<(WebSearchResult Result, int Score, int Index, string CanonicalUrl)>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in scored)
        {
            if (seenUrls.Add(item.CanonicalUrl))
            {
                uniqueByUrl.Add(item);
            }
        }

        // 2. Mirror and syndicated content deduplication (drop lower-scoring near-duplicate titles)
        var finalResults = new List<WebSearchResult>();
        foreach (var item in uniqueByUrl)
        {
            var isMirror = finalResults.Any(existing => UrlNormalization.AreTitlesNearDuplicates(existing.Title, item.Result.Title));
            if (!isMirror)
            {
                finalResults.Add(item.Result);
            }
        }

        return finalResults;
    }
}
