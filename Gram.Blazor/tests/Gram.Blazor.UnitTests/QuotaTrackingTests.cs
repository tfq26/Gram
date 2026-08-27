using System.Net;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class QuotaTrackingTests : IDisposable
{
    private readonly string _root;
    private readonly QuotaTracker _tracker;

    public QuotaTrackingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "gram_quota_" + Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataDir"] = _root })
            .Build();
        _tracker = new QuotaTracker(new StorageService(configuration), NullLogger<QuotaTracker>.Instance);
    }

    [Fact]
    public void ProviderHeaders_EmitEachLowQuotaThresholdOnce()
    {
        var key = QuotaRequestKey.Create("groq", "openai/gpt-oss-120b", "system", "system-secret");
        var notifications = new List<QuotaNotification>();
        _tracker.OnNotification += notifications.Add;

        _tracker.RecordResponse(key, Response(100, 100, 100, 100), 10);
        _tracker.RecordResponse(key, Response(100, 24, 100, 24), 10);
        _tracker.RecordResponse(key, Response(100, 9, 100, 9), 10);
        _tracker.RecordResponse(key, Response(100, 4, 100, 4), 10);
        _tracker.RecordResponse(key, Response(100, 4, 100, 4), 10);

        Assert.Equal([25, 10, 5], notifications.Where(item => !item.IsExhausted).Select(item => item.ThresholdPercent));
        Assert.Equal(4, _tracker.GetSnapshots().Single().RemainingPercent);
    }

    [Fact]
    public void RateLimitResponse_EmitsExhaustedNotification_AndBlocksUntilCooldown()
    {
        var key = QuotaRequestKey.Create("groq", "openai/gpt-oss-120b", "system", "system-secret");
        var notifications = new List<QuotaNotification>();
        _tracker.OnNotification += notifications.Add;
        var response = Response(100, 0, 100, 0);
        response.StatusCode = HttpStatusCode.TooManyRequests;
        response.Headers.TryAddWithoutValidation("retry-after", "60");

        _tracker.RecordResponse(key, response, 10);

        Assert.Contains(notifications, item => item.IsExhausted);
        Assert.False(_tracker.CanRequest(key, out var reason));
        Assert.Contains("cooling down", reason, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage Response(int requestLimit, int requestsRemaining, int tokenLimit, int tokensRemaining)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("x-ratelimit-limit-requests", requestLimit.ToString());
        response.Headers.TryAddWithoutValidation("x-ratelimit-remaining-requests", requestsRemaining.ToString());
        response.Headers.TryAddWithoutValidation("x-ratelimit-limit-tokens", tokenLimit.ToString());
        response.Headers.TryAddWithoutValidation("x-ratelimit-remaining-tokens", tokensRemaining.ToString());
        response.Headers.TryAddWithoutValidation("x-ratelimit-reset-requests", "60s");
        response.Headers.TryAddWithoutValidation("x-ratelimit-reset-tokens", "60s");
        return response;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
