using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public sealed record QuotaRequestKey(string Provider, string Model, string Scope, string AccountFingerprint)
{
    public string StableId => $"{Scope}:{Provider}:{Model}:{AccountFingerprint}";

    public static QuotaRequestKey Create(string provider, string model, string scope, string? apiKey)
    {
        var fingerprint = scope.Equals("system", StringComparison.OrdinalIgnoreCase)
            ? "system"
            : Fingerprint(apiKey);
        return new QuotaRequestKey(provider, model, scope, fingerprint);
    }

    private static string Fingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unconfigured";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();
    }
}

public sealed class QuotaSnapshot
{
    public string Provider { get; init; } = "";
    public string Model { get; init; } = "";
    public string Scope { get; init; } = "";
    public int? RequestsLimit { get; set; }
    public int? RequestsRemaining { get; set; }
    public long? TokensLimit { get; set; }
    public long? TokensRemaining { get; set; }
    public DateTimeOffset? RequestsResetAt { get; set; }
    public DateTimeOffset? TokensResetAt { get; set; }
    public DateTimeOffset? CooldownUntil { get; set; }
    public bool IsExhausted { get; set; }
    public int? LastStatusCode { get; set; }
    public long ObservedRequests { get; set; }
    public long ObservedInputTokens { get; set; }
    public long ObservedOutputTokens { get; set; }
    public DateTimeOffset ObservedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Confidence { get; set; } = "unknown";
    public int? RemainingPercent
    {
        get
        {
            var requestRatio = RequestsLimit > 0 && RequestsRemaining.HasValue
                ? RequestsRemaining.Value * 100d / RequestsLimit.Value
                : (double?)null;
            var tokenRatio = TokensLimit > 0 && TokensRemaining.HasValue
                ? TokensRemaining.Value * 100d / TokensLimit.Value
                : (double?)null;
            var ratio = new[] { requestRatio, tokenRatio }.Where(value => value.HasValue).Select(value => value!.Value).DefaultIfEmpty().Min();
            return requestRatio.HasValue || tokenRatio.HasValue ? Math.Clamp((int)Math.Floor(ratio), 0, 100) : null;
        }
    }
}

public sealed record QuotaNotification(
    QuotaRequestKey Key,
    QuotaSnapshot Snapshot,
    int? ThresholdPercent,
    bool IsExhausted,
    string Message);

public sealed class QuotaUsageEvent
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string Provider { get; init; } = "";
    public string Model { get; init; } = "";
    public string Scope { get; init; } = "";
    public string AccountFingerprint { get; init; } = "";
    public int StatusCode { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int? TotalTokens { get; init; }
    public long DurationMs { get; init; }
    public bool WasRateLimited { get; init; }
    public int? RetryAfterSeconds { get; init; }
}

public interface IQuotaTracker
{
    event Action<QuotaNotification>? OnNotification;
    bool CanRequest(QuotaRequestKey key, out string? reason);
    void RecordResponse(QuotaRequestKey key, HttpResponseMessage response, long durationMs);
    void RecordUsage(QuotaRequestKey key, int statusCode, int? inputTokens, int? outputTokens, int? totalTokens, long durationMs, bool wasRateLimited = false);
    IReadOnlyList<QuotaSnapshot> GetSnapshots();
}

public sealed class QuotaTracker : IQuotaTracker
{
    private static readonly int[] Thresholds = [25, 10, 5];
    private readonly StorageService _storage;
    private readonly ILogger<QuotaTracker> _logger;
    private readonly Dictionary<string, QuotaSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int?> _lastPercent = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _notifiedBoundaries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly string _eventsPath;

    public event Action<QuotaNotification>? OnNotification;

    public QuotaTracker(StorageService storage, ILogger<QuotaTracker> logger)
    {
        _storage = storage;
        _logger = logger;
        _eventsPath = _storage.GetFilePath("quota_usage.jsonl");
    }

    public bool CanRequest(QuotaRequestKey key, out string? reason)
    {
        lock (_sync)
        {
            if (!_snapshots.TryGetValue(key.StableId, out var snapshot))
            {
                reason = null;
                return true;
            }

            if (snapshot.CooldownUntil > DateTimeOffset.UtcNow)
            {
                var seconds = Math.Max(1, (int)Math.Ceiling((snapshot.CooldownUntil.Value - DateTimeOffset.UtcNow).TotalSeconds));
                reason = $"{key.Provider} quota is cooling down. Retry in about {seconds} seconds.";
                return false;
            }

            if (snapshot.RemainingPercent is <= 0)
            {
                reason = $"{key.Provider} quota is exhausted for {key.Model}.";
                return false;
            }
        }

        reason = null;
        return true;
    }

    public void RecordResponse(QuotaRequestKey key, HttpResponseMessage response, long durationMs)
    {
        var statusCode = (int)response.StatusCode;
        var retryAfter = ReadRetryAfterSeconds(response);
        var wasRateLimited = statusCode == 429;

        lock (_sync)
        {
            var snapshot = GetOrCreateSnapshot(key);
            ApplyHeaders(snapshot, response);
            snapshot.LastStatusCode = statusCode;
            if (wasRateLimited)
            {
                snapshot.IsExhausted = true;
                snapshot.CooldownUntil = DateTimeOffset.UtcNow.AddSeconds(retryAfter ?? 45);
            }
            else if (statusCode is >= 200 and < 300)
            {
                snapshot.IsExhausted = false;
                snapshot.CooldownUntil = null;
            }
            snapshot.ObservedAt = DateTimeOffset.UtcNow;
            snapshot.Confidence = HasProviderQuotaHeaders(response) ? "exact" : (wasRateLimited ? "observed" : snapshot.Confidence);
            NotifyThresholds(key, snapshot, wasRateLimited);
        }
    }

    public void RecordUsage(
        QuotaRequestKey key,
        int statusCode,
        int? inputTokens,
        int? outputTokens,
        int? totalTokens,
        long durationMs,
        bool wasRateLimited = false)
    {
        var usage = new QuotaUsageEvent
        {
            Provider = key.Provider,
            Model = key.Model,
            Scope = key.Scope,
            AccountFingerprint = key.AccountFingerprint,
            StatusCode = statusCode,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            DurationMs = durationMs,
            WasRateLimited = wasRateLimited || statusCode == 429,
            RetryAfterSeconds = GetRetryAfterForKey(key)
        };

        try
        {
            lock (_sync)
            {
                var snapshot = GetOrCreateSnapshot(key);
                snapshot.LastStatusCode = statusCode;
                if (wasRateLimited || statusCode == 429)
                {
                    snapshot.IsExhausted = true;
                    snapshot.CooldownUntil = DateTimeOffset.UtcNow.AddSeconds(usage.RetryAfterSeconds ?? 45);
                    NotifyThresholds(key, snapshot, true);
                }
                else if (statusCode is >= 200 and < 300)
                {
                    snapshot.IsExhausted = false;
                    snapshot.CooldownUntil = null;
                }
                snapshot.ObservedRequests++;
                snapshot.ObservedInputTokens += inputTokens ?? 0;
                snapshot.ObservedOutputTokens += outputTokens ?? 0;
                snapshot.ObservedAt = DateTimeOffset.UtcNow;
                if (snapshot.Confidence == "unknown") snapshot.Confidence = "observed";
                File.AppendAllText(_eventsPath, JsonSerializer.Serialize(usage) + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist quota usage event for {Provider}/{Model}.", key.Provider, key.Model);
        }
    }

    public IReadOnlyList<QuotaSnapshot> GetSnapshots()
    {
        lock (_sync)
        {
            return _snapshots.Values.Select(Clone).OrderBy(snapshot => snapshot.Scope).ThenBy(snapshot => snapshot.Provider).ThenBy(snapshot => snapshot.Model).ToList();
        }
    }

    private QuotaSnapshot GetOrCreateSnapshot(QuotaRequestKey key)
    {
        if (_snapshots.TryGetValue(key.StableId, out var existing)) return existing;
        var created = new QuotaSnapshot { Provider = key.Provider, Model = key.Model, Scope = key.Scope };
        _snapshots[key.StableId] = created;
        return created;
    }

    private void ApplyHeaders(QuotaSnapshot snapshot, HttpResponseMessage response)
    {
        snapshot.RequestsLimit = ReadIntHeader(response, "x-ratelimit-limit-requests") ?? snapshot.RequestsLimit;
        snapshot.RequestsRemaining = ReadIntHeader(response, "x-ratelimit-remaining-requests") ?? snapshot.RequestsRemaining;
        snapshot.TokensLimit = ReadLongHeader(response, "x-ratelimit-limit-tokens") ?? snapshot.TokensLimit;
        snapshot.TokensRemaining = ReadLongHeader(response, "x-ratelimit-remaining-tokens") ?? snapshot.TokensRemaining;

        var requestReset = ReadDurationHeader(response, "x-ratelimit-reset-requests");
        var tokenReset = ReadDurationHeader(response, "x-ratelimit-reset-tokens");
        if (requestReset.HasValue) snapshot.RequestsResetAt = DateTimeOffset.UtcNow.Add(requestReset.Value);
        if (tokenReset.HasValue) snapshot.TokensResetAt = DateTimeOffset.UtcNow.Add(tokenReset.Value);
    }

    private void NotifyThresholds(QuotaRequestKey key, QuotaSnapshot snapshot, bool wasRateLimited)
    {
        var percent = snapshot.RemainingPercent;
        var previous = _lastPercent.TryGetValue(key.StableId, out var prior) ? prior : null;
        _lastPercent[key.StableId] = percent;

        if (wasRateLimited || percent is <= 0)
        {
            Notify(key, snapshot, null, true, $"{key.Provider} quota is exhausted or rate-limited for {key.Model}.");
            return;
        }

        if (!percent.HasValue) return;
        foreach (var threshold in Thresholds)
        {
            if (percent.Value <= threshold && (!previous.HasValue || previous.Value > threshold))
            {
                Notify(key, snapshot, threshold, false, $"{key.Provider} quota for {key.Model} is at {percent.Value}% remaining.");
            }
        }
    }

    private void Notify(QuotaRequestKey key, QuotaSnapshot snapshot, int? threshold, bool exhausted, string message)
    {
        var reset = snapshot.RequestsResetAt?.ToUnixTimeSeconds() ?? snapshot.TokensResetAt?.ToUnixTimeSeconds() ?? 0;
        var boundary = $"{key.StableId}:{(threshold?.ToString() ?? "exhausted")}:{reset}";
        if (!_notifiedBoundaries.Add(boundary)) return;
        _logger.LogWarning("{Message} Scope={Scope}", message, key.Scope);
        OnNotification?.Invoke(new QuotaNotification(key, Clone(snapshot), threshold, exhausted, message));
    }

    private int? GetRetryAfterForKey(QuotaRequestKey key)
    {
        lock (_sync)
        {
            if (!_snapshots.TryGetValue(key.StableId, out var snapshot)) return null;
            var cooldownUntil = snapshot.CooldownUntil;
            if (!cooldownUntil.HasValue || cooldownUntil <= DateTimeOffset.UtcNow) return null;
            return Math.Max(1, (int)Math.Ceiling((cooldownUntil.Value - DateTimeOffset.UtcNow).TotalSeconds));
        }
    }

    private static bool HasProviderQuotaHeaders(HttpResponseMessage response) =>
        response.Headers.Contains("x-ratelimit-limit-requests") || response.Headers.Contains("x-ratelimit-remaining-requests") || response.Headers.Contains("x-ratelimit-limit-tokens") || response.Headers.Contains("x-ratelimit-remaining-tokens");

    private static int? ReadRetryAfterSeconds(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta) return Math.Max(1, (int)Math.Ceiling(delta.TotalSeconds));
        return null;
    }

    private static int? ReadIntHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) && int.TryParse(values.FirstOrDefault(), out var value) ? value : null;

    private static long? ReadLongHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) && long.TryParse(values.FirstOrDefault(), out var value) ? value : null;

    private static TimeSpan? ReadDurationHeader(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values)) return null;
        var raw = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (double.TryParse(raw, out var seconds)) return TimeSpan.FromSeconds(seconds);
        var match = Regex.Match(raw, @"^(?:(?<h>[0-9.]+)h)?(?:(?<m>[0-9.]+)m)?(?:(?<s>[0-9.]+)s)?$", RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        var hours = match.Groups["h"].Success ? double.Parse(match.Groups["h"].Value) : 0;
        var minutes = match.Groups["m"].Success ? double.Parse(match.Groups["m"].Value) : 0;
        var remainder = match.Groups["s"].Success ? double.Parse(match.Groups["s"].Value) : 0;
        return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(remainder);
    }

    private static QuotaSnapshot Clone(QuotaSnapshot source) => new()
    {
        Provider = source.Provider,
        Model = source.Model,
        Scope = source.Scope,
        RequestsLimit = source.RequestsLimit,
        RequestsRemaining = source.RequestsRemaining,
        TokensLimit = source.TokensLimit,
        TokensRemaining = source.TokensRemaining,
        RequestsResetAt = source.RequestsResetAt,
        TokensResetAt = source.TokensResetAt,
        CooldownUntil = source.CooldownUntil,
        ObservedRequests = source.ObservedRequests,
        ObservedInputTokens = source.ObservedInputTokens,
        ObservedOutputTokens = source.ObservedOutputTokens,
        ObservedAt = source.ObservedAt,
        IsExhausted = source.IsExhausted,
        LastStatusCode = source.LastStatusCode,
        Confidence = source.Confidence
    };
}
