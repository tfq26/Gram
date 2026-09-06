using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace Gram.Blazor.Shared;

public static class ConnectorEgressPolicy
{
    private static readonly HashSet<int> AllowedPorts = new() { 443, 8443 };

    public static async Task<string?> ValidateAsync(Uri uri, IReadOnlyList<string> allowedDomains, CancellationToken cancellationToken = default)
    {
        var (error, _) = await ValidateAndResolveAsync(uri, allowedDomains, cancellationToken);
        return error;
    }

    public static async Task<(string? Error, IPAddress? PinnedAddress)> ValidateAndResolveAsync(Uri uri, IReadOnlyList<string> allowedDomains, CancellationToken cancellationToken = default)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
            return ("Only HTTPS egress is allowed.", null);

        if (!AllowedPorts.Contains(uri.Port))
            return ($"Port {uri.Port} is not permitted for egress. Only standard HTTPS ports (443, 8443) are allowed.", null);

        var host = uri.IdnHost.TrimEnd('.');
        if (string.IsNullOrWhiteSpace(host))
            return ("Destination host is empty.", null);

        if (!allowedDomains.Any(domain => HostMatches(host, domain)))
            return ("Destination is not declared in the connector network policy.", null);

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch
        {
            return ("Destination DNS resolution failed.", null);
        }

        if (addresses.Length == 0)
            return ("Destination DNS resolved to no addresses.", null);

        foreach (var addr in addresses)
        {
            if (IsPrivateOrReserved(addr))
                return ("Destination resolved to a private, loopback, link-local, or reserved address.", null);
        }

        // Return first validated safe IP to pin against DNS rebinding
        return (null, addresses[0]);
    }

    public static bool HostMatches(string host, string domain)
    {
        var normalizedHost = host.Trim().TrimEnd('.');
        var normalizedDomain = domain.Trim().TrimEnd('.');
        if (normalizedDomain.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            normalizedDomain = normalizedDomain[4..];
        if (normalizedHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            normalizedHost = normalizedHost[4..];

        return normalizedHost.Equals(normalizedDomain, StringComparison.OrdinalIgnoreCase) ||
               normalizedHost.EndsWith("." + normalizedDomain, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPrivateOrReserved(IPAddress address)
    {
        // Normalize IPv4-mapped IPv6 address (e.g. ::ffff:127.0.0.1 -> 127.0.0.1)
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address)) return true;
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return true;
        if (address.Equals(IPAddress.IPv6Loopback) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None)) return true;

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            // 0.0.0.0/8 (Current network)
            if (bytes[0] == 0) return true;

            // 10.0.0.0/8 (RFC 1918 Private)
            if (bytes[0] == 10) return true;

            // 100.64.0.0/10 (RFC 6598 Shared Carrier-Grade NAT)
            if (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) return true;

            // 127.0.0.0/8 (Loopback)
            if (bytes[0] == 127) return true;

            // 169.254.0.0/16 (Link-local / Cloud metadata service e.g. 169.254.169.254)
            if (bytes[0] == 169 && bytes[1] == 254) return true;

            // 172.16.0.0/12 (RFC 1918 Private)
            if (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) return true;

            // 192.0.0.0/24 (IETF Protocol Assignments)
            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0) return true;

            // 192.0.2.0/24 (TEST-NET-1 Documentation)
            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2) return true;

            // 192.168.0.0/16 (RFC 1918 Private)
            if (bytes[0] == 192 && bytes[1] == 168) return true;

            // 198.18.0.0/15 (Benchmarking)
            if (bytes[0] == 198 && bytes[1] is 18 or 19) return true;

            // 198.51.100.0/24 (TEST-NET-2 Documentation)
            if (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) return true;

            // 203.0.113.0/24 (TEST-NET-3 Documentation)
            if (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) return true;

            // 224.0.0.0/4 (Multicast 224.0.0.0 - 239.255.255.255)
            if (bytes[0] is >= 224 and <= 239) return true;

            // 240.0.0.0/4 (Reserved for future use 240.0.0.0 - 255.255.255.254)
            if (bytes[0] >= 240) return true;

            // 255.255.255.255 (Limited Broadcast)
            if (bytes[0] == 255 && bytes[1] == 255 && bytes[2] == 255 && bytes[3] == 255) return true;

            return false;
        }

        // IPv6 Unique Local Address (fc00::/7 -> 0xfc or 0xfd)
        if (bytes[0] is 0xfc or 0xfd) return true;

        // IPv6 Documentation (2001:db8::/32)
        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8) return true;

        return false;
    }

    /// <summary>
    /// Creates an HttpMessageHandler that pins connections to a pre-validated IP address
    /// preventing DNS rebinding vulnerabilities and enforces destination domain policy across redirects.
    /// </summary>
    public static SocketsHttpHandler CreateSecureHandler(IReadOnlyList<string> allowedDomains, IPAddress pinnedAddress)
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false, // Validate every redirect hop manually
            ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(pinnedAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(pinnedAddress, context.DnsEndPoint.Port), cancellationToken);
                    var stream = new NetworkStream(socket, ownsSocket: true);
                    var sslStream = new SslStream(stream, false);
                    await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        TargetHost = context.DnsEndPoint.Host
                    }, cancellationToken);
                    return sslStream;
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
    }
}
