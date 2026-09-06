using System.Net;
using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ConnectorEgressPolicyTests
{
    [Fact]
    public void IsPrivateOrReserved_DetectsIPv4LoopbackAndPrivateRanges()
    {
        Assert.True(ConnectorEgressPolicy.IsPrivateOrReserved(IPAddress.Parse("127.0.0.1")));
        Assert.True(ConnectorEgressPolicy.IsPrivateOrReserved(IPAddress.Parse("10.0.1.5")));
        Assert.True(ConnectorEgressPolicy.IsPrivateOrReserved(IPAddress.Parse("172.20.0.1")));
        Assert.True(ConnectorEgressPolicy.IsPrivateOrReserved(IPAddress.Parse("192.168.1.1")));
        Assert.True(ConnectorEgressPolicy.IsPrivateOrReserved(IPAddress.Parse("169.254.169.254"))); // AWS/GCP metadata
    }

    [Fact]
    public void IsPrivateOrReserved_DetectsCGNAT_And_SpecialRanges()
    {
        Assert.True(ConnectorEgressPolicy.IsPrivateOrReserved(IPAddress.Parse("100.64.0.1"))); // Carrier-Grade NAT
        Assert.True(ConnectorEgressPolicy.IsPrivateOrReserved(IPAddress.Parse("100.127.255.255")));
        Assert.True(ConnectorEgressPolicy.IsPrivateOrReserved(IPAddress.Parse("0.0.0.0")));
        Assert.True(ConnectorEgressPolicy.IsPrivateOrReserved(IPAddress.Parse("224.0.0.1"))); // Multicast
        Assert.True(ConnectorEgressPolicy.IsPrivateOrReserved(IPAddress.Parse("240.0.0.1"))); // Reserved
        Assert.True(ConnectorEgressPolicy.IsPrivateOrReserved(IPAddress.Parse("255.255.255.255")));
    }

    [Fact]
    public void IsPrivateOrReserved_DetectsIPv4MappedIPv6PrivateAddresses()
    {
        // IPv4-mapped IPv6 bypass attempts e.g. ::ffff:127.0.0.1 or ::ffff:169.254.169.254
        var mappedLoopback = IPAddress.Parse("::ffff:127.0.0.1");
        var mappedMetadata = IPAddress.Parse("::ffff:169.254.169.254");
        var mappedPrivate = IPAddress.Parse("::ffff:10.0.0.1");

        Assert.True(ConnectorEgressPolicy.IsPrivateOrReserved(mappedLoopback));
        Assert.True(ConnectorEgressPolicy.IsPrivateOrReserved(mappedMetadata));
        Assert.True(ConnectorEgressPolicy.IsPrivateOrReserved(mappedPrivate));
    }

    [Fact]
    public void IsPrivateOrReserved_DetectsIPv6LoopbackAndULA()
    {
        Assert.True(ConnectorEgressPolicy.IsPrivateOrReserved(IPAddress.IPv6Loopback));
        Assert.True(ConnectorEgressPolicy.IsPrivateOrReserved(IPAddress.Parse("fc00::1"))); // ULA
        Assert.True(ConnectorEgressPolicy.IsPrivateOrReserved(IPAddress.Parse("fd12:3456:789a::1"))); // ULA
    }

    [Fact]
    public void IsPrivateOrReserved_AllowsPublicInternetAddresses()
    {
        Assert.False(ConnectorEgressPolicy.IsPrivateOrReserved(IPAddress.Parse("8.8.8.8")));
        Assert.False(ConnectorEgressPolicy.IsPrivateOrReserved(IPAddress.Parse("1.1.1.1")));
        Assert.False(ConnectorEgressPolicy.IsPrivateOrReserved(IPAddress.Parse("140.82.121.3"))); // GitHub
    }

    [Fact]
    public async Task ValidateAsync_RejectsHttpScheme()
    {
        var uri = new Uri("http://api.github.com/repos");
        var error = await ConnectorEgressPolicy.ValidateAsync(uri, ["github.com"]);
        Assert.Equal("Only HTTPS egress is allowed.", error);
    }

    [Fact]
    public async Task ValidateAsync_RejectsNonStandardPorts()
    {
        var uri = new Uri("https://api.github.com:22/ssh");
        var error = await ConnectorEgressPolicy.ValidateAsync(uri, ["github.com"]);
        Assert.Contains("Port 22 is not permitted", error ?? "");
    }

    [Fact]
    public async Task ValidateAsync_RejectsUndeclaredDomains()
    {
        var uri = new Uri("https://evil.com/exfiltrate");
        var error = await ConnectorEgressPolicy.ValidateAsync(uri, ["api.github.com"]);
        Assert.Contains("not declared in the connector network policy", error ?? "");
    }

    [Fact]
    public void HostMatches_SupportsSubdomainsAndNormalizesWww()
    {
        Assert.True(ConnectorEgressPolicy.HostMatches("api.github.com", "github.com"));
        Assert.True(ConnectorEgressPolicy.HostMatches("github.com", "github.com"));
        Assert.True(ConnectorEgressPolicy.HostMatches("www.github.com", "github.com"));
        Assert.False(ConnectorEgressPolicy.HostMatches("notgithub.com", "github.com"));
    }
}
