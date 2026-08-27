using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public sealed record ImageSearchResult(
    string Title,
    string ImageUrl,
    string SourcePageUrl,
    string? Creator = null,
    string? License = null,
    string? LicenseUrl = null,
    string? Description = null);

public interface IImageSearchProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<IReadOnlyList<ImageSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default);
}

/// <summary>Keyless image search using Wikimedia Commons, with license metadata.</summary>
public sealed class WikimediaImageSearchProvider : IImageSearchProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WikimediaImageSearchProvider> _logger;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly IRateLimitCoordinator? _rateLimiter;

    public WikimediaImageSearchProvider(IHttpClientFactory httpClientFactory, ILogger<WikimediaImageSearchProvider> logger, IRateLimitCoordinator? rateLimiter = null)
    {
        _httpClient = httpClientFactory.CreateClient("WebBrowser");
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _logger = logger;
        _rateLimiter = rateLimiter;
    }

    public string Name => "Wikimedia Commons";
    public bool IsConfigured => true;

    public async Task<IReadOnlyList<ImageSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || maxResults <= 0) return [];
        var url = "https://commons.wikimedia.org/w/api.php?action=query&generator=search&gsrnamespace=6&gsrsearch="
            + Uri.EscapeDataString(query.Trim())
            + $"&gsrlimit={Math.Clamp(maxResults, 1, 10)}&prop=imageinfo|info&inprop=url&iiprop=url|extmetadata&iiurlwidth=1600&format=json&origin=*";
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("Gram-Assistant/1.0 (presentation asset search)");
                using var lease = _rateLimiter is null ? null : await _rateLimiter.AcquireAsync(Name, "search", cancellationToken);
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                _rateLimiter?.Observe(Name, "search", (int)response.StatusCode, response.Headers.RetryAfter?.Delta is { } retryDelta ? (int)Math.Ceiling(retryDelta.TotalSeconds) : null);
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 2)
                {
                    var seconds = response.Headers.RetryAfter?.Delta is { } delta
                        ? Math.Clamp((int)Math.Ceiling(delta.TotalSeconds), 1, 15)
                        : 2 * (attempt + 1);
                    await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
                    continue;
                }
                if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Wikimedia returned HTTP {(int)response.StatusCode}.");

                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                if (!document.RootElement.TryGetProperty("query", out var queryElement)
                    || !queryElement.TryGetProperty("pages", out var pages)) return [];

                var results = new List<ImageSearchResult>();
                foreach (var page in pages.EnumerateObject())
                {
                    if (!page.Value.TryGetProperty("imageinfo", out var imageInfo) || imageInfo.GetArrayLength() == 0) continue;
                    var info = imageInfo[0];
                    var imageUrl = ReadString(info, "thumburl") ?? ReadString(info, "url");
                    var sourceUrl = ReadString(page.Value, "canonicalurl") ?? ReadString(page.Value, "fullurl");
                    if (!IsAllowedImageUrl(imageUrl) || !Uri.TryCreate(sourceUrl, UriKind.Absolute, out _)) continue;
                    var metadata = info.TryGetProperty("extmetadata", out var ext) ? ext : default;
                    results.Add(new ImageSearchResult(
                        WebUtility.HtmlDecode(page.Value.TryGetProperty("title", out var title) ? title.GetString() : "Image")?.Replace("File:", "", StringComparison.OrdinalIgnoreCase).Trim() ?? "Image",
                        imageUrl!, sourceUrl!, ReadMetadata(metadata, "Artist"), ReadMetadata(metadata, "LicenseShortName"),
                        ReadMetadata(metadata, "LicenseUrl"), ReadMetadata(metadata, "ImageDescription")));
                }
                _logger.LogInformation("Wikimedia image search returned {Count} result(s) for {Query}.", results.Count, query);
                return results;
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? ReadMetadata(JsonElement metadata, string property) =>
        metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty(property, out var value)
            ? ReadString(value, "value")
            : null;

    private static bool IsAllowedImageUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("upload.wikimedia.org", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".wikimedia.org", StringComparison.OrdinalIgnoreCase));
}

public sealed record CachedImageAsset(ContentAsset Asset, ImageSearchResult Source);

public sealed class PresentationImageAssetService
{
    private const long MaxImageBytes = 12 * 1024 * 1024;
    private readonly IEnumerable<IImageSearchProvider> _providers;
    private readonly HttpClient _httpClient;
    private readonly StorageService _storage;
    private readonly ILogger<PresentationImageAssetService> _logger;

    public PresentationImageAssetService(IEnumerable<IImageSearchProvider> providers, IHttpClientFactory httpClientFactory, StorageService storage, ILogger<PresentationImageAssetService> logger)
    {
        _providers = providers;
        _httpClient = httpClientFactory.CreateClient("WebBrowser");
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
        _storage = storage;
        _logger = logger;
    }

    public async Task<CachedImageAsset?> SearchAndCacheAsync(string visualBrief, string projectId, string sectionId, CancellationToken cancellationToken = default)
    {
        var query = BuildQuery(visualBrief);
        if (string.IsNullOrWhiteSpace(query)) return null;
        foreach (var provider in _providers.Where(item => item.IsConfigured))
        {
            try
            {
                var result = (await provider.SearchAsync(query, 1, cancellationToken)).FirstOrDefault();
                if (result is null) continue;
                using var response = await _httpClient.GetAsync(result.ImageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxImageBytes) continue;
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                var extension = ExtensionFor(response.Content.Headers.ContentType?.MediaType, result.ImageUrl);
                var directory = _storage.GetFilePath(Path.Combine("presentation-assets", projectId));
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, $"{sectionId}-{Guid.NewGuid():N}{extension}");
                await using var output = File.Create(path);
                await input.CopyToAsync(output, cancellationToken);
                var asset = new ContentAsset { Kind = "image", Label = result.Title, Path = path, AltText = result.Description ?? result.Title };
                _logger.LogInformation("Cached presentation image {Path} from {SourceUrl}.", path, result.SourcePageUrl);
                return new CachedImageAsset(asset, result);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException)
            {
                _logger.LogWarning(ex, "Image provider {Provider} failed for visual brief.", provider.Name);
            }
        }
        return null;
    }

    private static string BuildQuery(string visualBrief)
    {
        var query = visualBrief.Trim();
        query = System.Text.RegularExpressions.Regex.Replace(query, "\\s+", " ");
        return query.Length > 180 ? query[..180] : query;
    }

    private static string ExtensionFor(string? mediaType, string url) => mediaType?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => ".jpg"
    };
}
