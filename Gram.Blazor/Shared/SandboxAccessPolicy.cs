using System.Security.Cryptography;
using System.Text;

namespace Gram.Blazor.Shared;

public sealed record SandboxAccessSnapshot(bool Enabled, string Mode, string? DisabledReason = null, DateTimeOffset? ChangedAt = null);
public sealed record SandboxPolicyUpdateRequest(bool Enabled, string? DisabledReason = null);

public interface ISandboxAccessPolicy
{
    SandboxAccessSnapshot Current { get; }
    bool CanCreate(out string? reason);
    void Set(bool enabled, string? reason = null);
    bool IsAdminKeyValid(string? candidate);
}

/// Central runtime gate for sandbox creation. The flag is process-safe and can be
/// changed without restarting the application; billing/entitlement storage can
/// replace this implementation later without changing API consumers.
public sealed class SandboxAccessPolicy : ISandboxAccessPolicy
{
    private readonly string? _adminKey = Environment.GetEnvironmentVariable("GRAMBOX_ADMIN_KEY");
    private SandboxAccessSnapshot _current = new(
        !string.Equals(Environment.GetEnvironmentVariable("GRAMBOX_ENABLED"), "false", StringComparison.OrdinalIgnoreCase),
        "local-native");

    public SandboxAccessSnapshot Current => Volatile.Read(ref _current);

    public bool CanCreate(out string? reason)
    {
        var snapshot = Current;
        reason = snapshot.Enabled ? null : snapshot.DisabledReason ?? "Sandbox creation is currently disabled.";
        return snapshot.Enabled;
    }

    public void Set(bool enabled, string? reason = null) => Volatile.Write(ref _current, new SandboxAccessSnapshot(enabled, "local-native", enabled ? null : reason, DateTimeOffset.UtcNow));

    public bool IsAdminKeyValid(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(_adminKey) || string.IsNullOrWhiteSpace(candidate)) return false;
        var expected = Encoding.UTF8.GetBytes(_adminKey);
        var actual = Encoding.UTF8.GetBytes(candidate);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
