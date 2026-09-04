using System.Net;
using System.Net.Sockets;

namespace Gram.Blazor.Shared;

public static class ConnectorEgressPolicy
{
    public static async Task<string?> ValidateAsync(Uri uri, IReadOnlyList<string> allowedDomains, CancellationToken cancellationToken = default)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) return "Only HTTPS egress is allowed.";
        var host = uri.IdnHost.TrimEnd('.');
        if (!allowedDomains.Any(domain => HostMatches(host, domain))) return "Destination is not declared in the connector network policy.";
        IPAddress[] addresses;
        try { addresses = await Dns.GetHostAddressesAsync(host, cancellationToken); }
        catch { return "Destination DNS resolution failed."; }
        if (addresses.Length == 0 || addresses.Any(IsPrivate)) return "Destination resolved to a private, loopback, or link-local address.";
        return null;
    }

    private static bool HostMatches(string host, string domain)
    {
        var normalized = domain.Trim().TrimEnd('.');
        return host.Equals(normalized, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return true;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 || bytes[0] == 127 || bytes[0] == 169 && bytes[1] == 254 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168;
        }
        return address.Equals(IPAddress.IPv6Loopback) || address.GetAddressBytes()[0] is 0xfc or 0xfd;
    }
}
