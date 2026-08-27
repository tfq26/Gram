using Gram.Blazor.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class RateLimitCoordinatorTests
{
    [Fact]
    public async Task SameProviderAndOperation_AreSerialized()
    {
        var limiter = new RateLimitCoordinator(NullLogger<RateLimitCoordinator>.Instance);
        var first = await limiter.AcquireAsync("test", "model");
        var waiting = limiter.AcquireAsync("test", "model");
        await Task.Delay(25);
        Assert.False(waiting.IsCompleted);
        first.Dispose();
        using var second = await waiting;
    }

    [Fact]
    public async Task RateLimitObservation_IsAppliedToNextRequest()
    {
        var limiter = new RateLimitCoordinator(NullLogger<RateLimitCoordinator>.Instance);
        using (await limiter.AcquireAsync("test", "search"))
        {
            limiter.Observe("test", "search", 429, 1);
        }

        var started = DateTimeOffset.UtcNow;
        using var lease = await limiter.AcquireAsync("test", "search");
        Assert.True((DateTimeOffset.UtcNow - started).TotalMilliseconds >= 800);
    }
}
