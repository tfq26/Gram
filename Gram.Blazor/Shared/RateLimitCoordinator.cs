using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public interface IRateLimitCoordinator
{
    Task<IDisposable> AcquireAsync(string provider, string operation, CancellationToken cancellationToken = default);
    void Observe(string provider, string operation, int statusCode, int? retryAfterSeconds = null);
}

/// <summary>Process-wide limiter for shared system keys and public services.</summary>
public sealed class RateLimitCoordinator : IRateLimitCoordinator
{
    private sealed class State { public readonly SemaphoreSlim Gate = new(1, 1); public DateTimeOffset NextAllowed; public int ConsecutiveFailures; }
    private readonly Dictionary<string, State> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly ILogger<RateLimitCoordinator> _logger;

    public RateLimitCoordinator(ILogger<RateLimitCoordinator> logger) => _logger = logger;

    public async Task<IDisposable> AcquireAsync(string provider, string operation, CancellationToken cancellationToken = default)
    {
        var key = $"{provider}:{operation}";
        State state;
        lock (_sync) { if (!_states.TryGetValue(key, out state!)) _states[key] = state = new State(); }
        await state.Gate.WaitAsync(cancellationToken);
        var delay = state.NextAllowed - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
        return new Lease(state);
    }

    public void Observe(string provider, string operation, int statusCode, int? retryAfterSeconds = null)
    {
        var key = $"{provider}:{operation}";
        State state;
        lock (_sync) { if (!_states.TryGetValue(key, out state!)) _states[key] = state = new State(); }
        if (statusCode is 429 or 503)
        {
            state.ConsecutiveFailures = Math.Min(state.ConsecutiveFailures + 1, 6);
            var seconds = retryAfterSeconds ?? Math.Min(30, Math.Pow(2, state.ConsecutiveFailures));
            state.NextAllowed = DateTimeOffset.UtcNow.AddSeconds(seconds);
            _logger.LogWarning("Rate limiter backing off {Provider}/{Operation} for {Seconds}s after HTTP {Status}.", provider, operation, seconds, statusCode);
        }
        else if (statusCode is >= 200 and < 400)
        {
            state.ConsecutiveFailures = 0;
            state.NextAllowed = DateTimeOffset.UtcNow.AddMilliseconds(250);
        }
    }

    private sealed class Lease(State state) : IDisposable
    {
        public void Dispose() => state.Gate.Release();
    }
}
