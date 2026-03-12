using System.Net;
using RemoteLink.Shared.Services;

namespace RemoteLink.Shared.Tests.Services;

public class NetworkAddressResolverTests
{
    [Fact]
    public void SelectPreferredIPv4Address_PrefersPhysicalGatewayAddress()
    {
        var candidates = new[]
        {
            new NetworkInterfaceAddressCandidate(IPAddress.Parse("172.24.224.1"), IPAddress.Parse("172.24.255.255"), true, true, 1200),
            new NetworkInterfaceAddressCandidate(IPAddress.Parse("192.168.1.42"), IPAddress.Parse("192.168.1.255"), true, false, 1300)
        };

        var address = NetworkAddressResolver.SelectPreferredIPv4Address(candidates);

        Assert.Equal("192.168.1.42", address?.ToString());
    }

    [Fact]
    public void SelectPreferredIPv4Address_FallsBackToNonVirtualAddressWithoutGateway()
    {
        var candidates = new[]
        {
            new NetworkInterfaceAddressCandidate(IPAddress.Parse("10.10.0.5"), IPAddress.Parse("10.10.0.255"), false, true, 500),
            new NetworkInterfaceAddressCandidate(IPAddress.Parse("192.168.50.10"), IPAddress.Parse("192.168.50.255"), false, false, 400)
        };

        var address = NetworkAddressResolver.SelectPreferredIPv4Address(candidates);

        Assert.Equal("192.168.50.10", address?.ToString());
    }

    [Fact]
    public void SelectBroadcastEndpoints_ReturnsFallbackBroadcastWhenNoCandidatesExist()
    {
        var endpoints = NetworkAddressResolver.SelectBroadcastEndpoints(Array.Empty<NetworkInterfaceAddressCandidate>(), 12345);

        var endpoint = Assert.Single(endpoints);
        Assert.Equal(IPAddress.Broadcast, endpoint.Address);
        Assert.Equal(12345, endpoint.Port);
    }
}
