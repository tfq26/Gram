using System.Text.RegularExpressions;

namespace Gram.Blazor.Shared;

public sealed record GroundedAnswerValidation(
    bool Passed,
    int MaterialClaimCount,
    int CitedMaterialClaimCount,
    int ValidCitationCount,
    int InvalidCitationCount,
    IReadOnlyList<string> Issues)
{
    public double CitationCoverage => MaterialClaimCount == 0 ? 1 : (double)CitedMaterialClaimCount / MaterialClaimCount;
}

/// <summary>
/// Deterministic safety net for research answers. It does not judge prose quality;
/// it checks citation provenance and citation coverage for material claims.
/// </summary>
public static class GroundedAnswerValidator
{
    private static readonly Regex CitationRegex = new(@"\[[^\]]+\]\((https?://[^)\s]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MaterialClaimRegex = new(@"\b(current|latest|effective|expires?|regulation|law|tariff|tax|credit|percent|\d{4}|\d+%|\$\s?\d|€\s?\d)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static GroundedAnswerValidation Validate(string? answer, IReadOnlyList<WebSearchResult> sources)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(answer)) return new(false, 0, 0, 0, 0, ["The answer was empty."]);

        var sourceUrls = sources.Select(source => NormalizeUrl(source.Url)).Where(url => url.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var citations = CitationRegex.Matches(answer).Select(match => NormalizeUrl(match.Groups[1].Value)).Where(url => url.Length > 0).ToList();
        var validCitations = citations.Count(sourceUrls.Contains);
        var invalidCitations = citations.Count - validCitations;
        if (invalidCitations > 0) issues.Add($"{invalidCitations} citation(s) do not point to a retrieved source.");

        // Keep a paragraph together so citations placed at the end of a sentence
        // still cover the preceding claim without splitting URLs on punctuation.
        var claims = Regex.Split(answer, @"\r?\n+", RegexOptions.Compiled)
            .Select(claim => claim.Trim())
            .Where(claim => claim.Length > 0)
            .Where(IsMaterialClaim)
            .ToList();
        var citedClaims = claims.Count(claim => CitationRegex.IsMatch(claim) && CitationRegex.Matches(claim).Cast<Match>().Any(match => sourceUrls.Contains(NormalizeUrl(match.Groups[1].Value))));
        if (claims.Count > citedClaims) issues.Add($"{claims.Count - citedClaims} material claim(s) lack a citation to retrieved evidence.");

        var passed = issues.Count == 0 && (claims.Count == 0 || citedClaims == claims.Count);
        return new GroundedAnswerValidation(passed, claims.Count, citedClaims, validCitations, invalidCitations, issues);
    }

    private static bool IsMaterialClaim(string text) => MaterialClaimRegex.IsMatch(text);

    private static string NormalizeUrl(string url)
    {
        var normalized = url.Trim().TrimEnd('.', ',', ';', ':');
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.ToString().TrimEnd('/')
            : "";
    }
}
