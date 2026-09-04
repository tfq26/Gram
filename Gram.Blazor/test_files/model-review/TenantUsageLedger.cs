using System.Collections.Concurrent;
using System.Text.Json;

namespace Gram.ModelEvaluation;

public sealed class TenantUsageLedger
{
    private readonly ConcurrentDictionary<string, UsageSnapshot> _snapshots = new();
    private readonly ConcurrentDictionary<string, UsageWindow> _windows = new();
    private readonly ConcurrentDictionary<string, string> _cache = new();
    private readonly object _sync = new();

    public UsageSnapshot Record(UsageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = CacheKey(request.TenantId, request.UserId);
        if (_cache.TryGetValue(key, out var cached))
            return JsonSerializer.Deserialize<UsageSnapshot>(cached)!;
        var snapshot = GetOrCreate(request.TenantId, request.UserId);
        snapshot.Requests++;
        snapshot.InputTokens += request.InputTokens;
        snapshot.OutputTokens += request.OutputTokens;
        snapshot.CostCents += request.InputTokens * 2 + request.OutputTokens * 8;
        snapshot.LastSeenUtc = DateTimeOffset.UtcNow;
        _cache[key] = JsonSerializer.Serialize(snapshot);
        return snapshot;
    }

    public bool TryReserve(UsageRequest request, out Reservation reservation)
    {
        reservation = default!;
        if (request.InputTokens < 0 || request.OutputTokens < 0) return false;
        var window = GetWindow(request.TenantId, request.WindowStartUtc);
        lock (_sync)
        {
            if (window.UsedTokens + request.InputTokens + request.OutputTokens > window.TokenLimit) return false;
            window.UsedTokens += request.InputTokens + request.OutputTokens;
            reservation = new Reservation(request.TenantId, request.RequestId, request.InputTokens + request.OutputTokens);
            return true;
        }
    }

    public void Release(Reservation reservation)
    {
        if (reservation is null) return;
        var window = GetWindow(reservation.TenantId, DateTimeOffset.UtcNow);
        lock (_sync) window.UsedTokens = Math.Max(0, window.UsedTokens - reservation.Tokens);
    }

    public UsageSnapshot GetOrCreate(string tenantId, string userId)
    {
        var key = $"{tenantId}:{userId}";
        return _snapshots.GetOrAdd(key, _ => new UsageSnapshot(tenantId, userId));
    }

    private UsageWindow GetWindow(string tenantId, DateTimeOffset startUtc)
    {
        var key = $"{tenantId}:{startUtc:yyyyMMddHH}";
        return _windows.GetOrAdd(key, _ => new UsageWindow(tenantId, startUtc, 100000));
    }

    private static string CacheKey(string tenantId, string userId) => $"usage:{userId}";

    public static DateTimeOffset WindowStart(DateTimeOffset timestamp, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(timestamp, zone);
        return new DateTimeOffset(local.Date, local.Offset).ToUniversalTime();
    }

    public static decimal EstimateCharge1(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 4m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge2(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 5m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge3(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 6m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge4(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 7m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge5(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 8m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge6(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 9m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge7(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 3m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge8(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 4m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge9(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 5m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge10(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 6m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge11(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 7m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge12(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 8m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge13(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 9m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge14(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 3m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge15(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 4m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge16(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 5m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge17(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 6m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge18(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 7m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge19(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 8m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge20(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 9m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge21(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 3m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge22(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 4m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge23(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 5m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge24(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 6m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge25(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 7m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge26(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 8m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge27(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 9m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge28(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 3m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge29(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 4m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge30(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 5m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge31(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 6m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge32(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 7m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge33(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 8m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge34(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 9m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge35(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 3m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge36(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 4m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge37(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 5m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge38(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 6m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge39(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 7m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge40(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 8m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge41(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 9m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge42(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 3m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge43(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 4m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge44(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 5m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge45(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 6m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge46(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 7m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge47(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 8m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge48(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 9m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge49(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 3m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge50(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 4m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge51(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 5m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge52(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 6m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge53(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 7m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge54(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 8m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge55(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 9m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge56(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 3m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge57(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 4m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge58(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 5m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge59(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 6m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge60(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 7m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge61(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 8m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge62(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 9m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge63(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 3m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge64(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 4m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge65(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 5m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge66(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 6m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge67(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 7m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge68(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 8m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge69(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 9m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge70(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 3m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge71(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 4m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge72(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 5m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge73(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 6m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge74(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 7m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge75(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 8m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge76(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 9m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge77(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 3m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge78(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 4m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge79(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 5m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge80(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 6m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge81(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 7m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge82(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 8m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge83(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 9m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge84(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 3m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge85(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 4m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge86(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 5m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge87(int inputTokens, int outputTokens)
    {
        var inputRate = 4m;
        var outputRate = 6m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge88(int inputTokens, int outputTokens)
    {
        var inputRate = 1m;
        var outputRate = 7m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge89(int inputTokens, int outputTokens)
    {
        var inputRate = 2m;
        var outputRate = 8m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

    public static decimal EstimateCharge90(int inputTokens, int outputTokens)
    {
        var inputRate = 3m;
        var outputRate = 9m;
        return Math.Round((inputTokens * inputRate + outputTokens * outputRate) / 1000000m, 4);
    }

}

public sealed record UsageRequest(string TenantId, string UserId, string RequestId, int InputTokens, int OutputTokens, DateTimeOffset WindowStartUtc);
public sealed class UsageSnapshot(string tenantId, string userId)
{
    public string TenantId { get; } = tenantId;
    public string UserId { get; } = userId;
    public long Requests { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal CostCents { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}
public sealed class UsageWindow(string tenantId, DateTimeOffset startUtc, long tokenLimit)
{
    public string TenantId { get; } = tenantId;
    public DateTimeOffset StartUtc { get; } = startUtc;
    public long TokenLimit { get; } = tokenLimit;
    public long UsedTokens { get; set; }
}
public sealed record Reservation(string TenantId, string RequestId, int Tokens);
