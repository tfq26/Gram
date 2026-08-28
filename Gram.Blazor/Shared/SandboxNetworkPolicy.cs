using System.Text.RegularExpressions;

namespace Gram.Blazor.Shared;

public static class SandboxNetworkPolicy
{
    private static readonly Regex DirectNetworkCommand = new(@"(^|[;&|])\s*(curl|wget|nc|netcat|ssh|scp|sftp|ftp|telnet)\b|\bgit\s+(clone|fetch|pull)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UrlHost = new("https?://([^/\\s:'\\\"]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] SecretMarkers = ["_API_KEY", "_TOKEN", "_SECRET", "_PASSWORD", "_PASS", "_PRIVATE_KEY", "_CREDENTIAL"];

    public static bool CanExecuteCommand(string command, SandboxNetworkMode mode, IReadOnlyList<string> allowedHosts, out string? reason)
    {
        reason = null;
        if (mode == SandboxNetworkMode.Unrestricted || !DirectNetworkCommand.IsMatch(command)) return true;
        if (mode == SandboxNetworkMode.Disabled)
        {
            reason = "Direct network commands are disabled in this sandbox.";
            return false;
        }

        var hosts = UrlHost.Matches(command).Select(match => match.Groups[1].Value.ToLowerInvariant()).ToList();
        if (hosts.Count == 0) return true;
        var allowed = allowedHosts.Select(host => host.Trim().TrimStart('.').ToLowerInvariant()).Where(host => host.Length > 0).ToList();
        if (hosts.All(host => allowed.Any(rule => host.Equals(rule, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + rule, StringComparison.OrdinalIgnoreCase)))) return true;
        reason = $"Network destination is not in the sandbox allowlist: {string.Join(", ", hosts)}.";
        return false;
    }

    public static bool IsSensitiveEnvironmentVariable(string name) =>
        SecretMarkers.Any(marker => name.EndsWith(marker, StringComparison.OrdinalIgnoreCase))
        || name.Contains("SECRET", StringComparison.OrdinalIgnoreCase)
        || name.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)
        || name.Contains("PRIVATE_KEY", StringComparison.OrdinalIgnoreCase);
}
