using System.Text.RegularExpressions;
using System.Web;

namespace Gram.Blazor.Shared;

public static class UrlNormalization
{
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
        "ref", "source", "fbclid", "gclid", "ocid", "ved", "usqp", "igshid", "mc_cid", "mc_eid"
    };

    /// <summary>
    /// Normalizes URLs by lowercasing scheme/host, stripping 'www.', removing default ports,
    /// removing tracking parameters (utm_*, ref, etc.), removing URL fragments, and normalizing trailing slashes.
    /// </summary>
    public static string CanonicalizeUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return string.Empty;
        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri)) return rawUrl.Trim();

        var scheme = uri.Scheme.ToLowerInvariant();
        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.")) host = host[4..];

        var portPart = (uri.IsDefaultPort || (scheme == "http" && uri.Port == 80) || (scheme == "https" && uri.Port == 443))
            ? string.Empty
            : $":{uri.Port}";

        var path = uri.AbsolutePath;
        if (path.Length > 1 && path.EndsWith('/'))
        {
            path = path.TrimEnd('/');
        }

        var query = string.Empty;
        if (!string.IsNullOrEmpty(uri.Query))
        {
            var parsed = HttpUtility.ParseQueryString(uri.Query);
            var filteredKeys = parsed.AllKeys
                .Where(k => !string.IsNullOrWhiteSpace(k) && !TrackingParameters.Contains(k) && !k.StartsWith("utm_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (filteredKeys.Count > 0)
            {
                var pairs = filteredKeys.Select(k => $"{Uri.EscapeDataString(k!)}={Uri.EscapeDataString(parsed[k] ?? string.Empty)}");
                query = "?" + string.Join("&", pairs);
            }
        }

        return $"{scheme}://{host}{portPart}{path}{query}";
    }

    /// <summary>
    /// Determines whether the given URL matches any of the allowed/preferred domains or their subdomains.
    /// </summary>
    public static bool IsDomainAllowed(string? url, IReadOnlyList<string>? allowedDomains)
    {
        if (allowedDomains == null || allowedDomains.Count == 0) return true;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.")) host = host[4..];

        foreach (var allowed in allowedDomains)
        {
            if (string.IsNullOrWhiteSpace(allowed)) continue;
            var cleanAllowed = allowed.Trim().ToLowerInvariant();
            if (cleanAllowed.StartsWith("www.")) cleanAllowed = cleanAllowed[4..];

            if (host.Equals(cleanAllowed, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + cleanAllowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if two titles are near duplicates (e.g. syndicated mirrors).
    /// </summary>
    public static bool AreTitlesNearDuplicates(string? titleA, string? titleB, double threshold = 0.85)
    {
        if (string.IsNullOrWhiteSpace(titleA) || string.IsNullOrWhiteSpace(titleB)) return false;
        var tokensA = Tokenize(titleA);
        var tokensB = Tokenize(titleB);
        if (tokensA.Count == 0 || tokensB.Count == 0) return false;

        var intersection = tokensA.Intersect(tokensB).Count();
        var union = tokensA.Union(tokensB).Count();
        return (double)intersection / union >= threshold;
    }

    private static HashSet<string> Tokenize(string text)
    {
        var words = Regex.Matches(text.ToLowerInvariant(), @"\b[a-z0-9]+\b")
            .Select(m => m.Value);
        return new HashSet<string>(words);
    }
}
