using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RemoteLink.Shared.Services;

public sealed record NetworkInterfaceAddressCandidate(
    IPAddress Address,
    IPAddress BroadcastAddress,
    bool HasGateway,
    bool IsVirtual,
    int Priority);

public static class NetworkAddressResolver
{
    private static readonly string[] VirtualAdapterKeywords =
    [
        "virtual",
        "hyper-v",
        "vethernet",
        "vmware",
        "virtualbox",
        "docker",
        "wsl",
        "loopback",
        "tunnel"
    ];

    public static string? GetPreferredIPv4Address()
        => SelectPreferredIPv4Address(GetInterfaceCandidates())?.ToString();

    public static IReadOnlyList<IPEndPoint> GetBroadcastEndpoints(int port)
        => SelectBroadcastEndpoints(GetInterfaceCandidates(), port);

    public static IPAddress? SelectPreferredIPv4Address(IEnumerable<NetworkInterfaceAddressCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(candidate => IsUsableAddress(candidate.Address))
            .OrderByDescending(candidate => candidate.HasGateway)
            .ThenBy(candidate => candidate.IsVirtual)
            .ThenByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Address.ToString(), StringComparer.Ordinal)
            .Select(candidate => candidate.Address)
            .FirstOrDefault();
    }

    public static IReadOnlyList<IPEndPoint> SelectBroadcastEndpoints(IEnumerable<NetworkInterfaceAddressCandidate> candidates, int port)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var endpoints = candidates
            .Where(candidate => IsUsableAddress(candidate.Address))
            .OrderByDescending(candidate => candidate.HasGateway)
            .ThenBy(candidate => candidate.IsVirtual)
            .ThenByDescending(candidate => candidate.Priority)
            .GroupBy(candidate => candidate.BroadcastAddress)
            .Select(group => new IPEndPoint(group.Key, port))
            .ToList();

        if (endpoints.Count == 0)
            endpoints.Add(new IPEndPoint(IPAddress.Broadcast, port));

        return endpoints;
    }

    private static IEnumerable<NetworkInterfaceAddressCandidate> GetInterfaceCandidates()
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                !networkInterface.Supports(NetworkInterfaceComponent.IPv4))
            {
                continue;
            }

            IPInterfaceProperties properties;
            try
            {
                properties = networkInterface.GetIPProperties();
            }
            catch
            {
                continue;
            }

            bool hasGateway;
            try
            {
                hasGateway = properties.GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.Any.Equals(gateway.Address) &&
                    !IPAddress.None.Equals(gateway.Address));
            }
            catch (PlatformNotSupportedException)
            {
                hasGateway = false;
            }
            var isVirtual = IsProbablyVirtual(networkInterface);
            var priority = GetPriority(networkInterface, hasGateway);

            foreach (var unicastAddress in properties.UnicastAddresses)
            {
                if (unicastAddress.Address.AddressFamily != AddressFamily.InterNetwork ||
                    !IsUsableAddress(unicastAddress.Address) ||
                    !TryGetBroadcastAddress(unicastAddress, out var broadcastAddress))
                {
                    continue;
                }

                yield return new NetworkInterfaceAddressCandidate(
                    unicastAddress.Address,
                    broadcastAddress,
                    hasGateway,
                    isVirtual,
                    priority);
            }
        }
    }

    private static bool TryGetBroadcastAddress(UnicastIPAddressInformation addressInformation, out IPAddress broadcastAddress)
    {
        broadcastAddress = IPAddress.Broadcast;

        var mask = addressInformation.IPv4Mask;
        if (mask is null || Equals(mask, IPAddress.Any))
        {
            var prefixLength = addressInformation.PrefixLength;
            if (prefixLength is <= 0 or >= 32)
                return false;

            mask = CreateMaskFromPrefixLength(prefixLength);
        }

        var addressBytes = addressInformation.Address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        if (addressBytes.Length != 4 || maskBytes.Length != 4)
            return false;

        var broadcastBytes = new byte[4];
        for (var index = 0; index < 4; index++)
            broadcastBytes[index] = (byte)(addressBytes[index] | ~maskBytes[index]);

        broadcastAddress = new IPAddress(broadcastBytes);
        return true;
    }

    private static IPAddress CreateMaskFromPrefixLength(int prefixLength)
    {
        var mask = prefixLength == 0
            ? 0u
            : uint.MaxValue << (32 - prefixLength);

        return new IPAddress(BitConverter.GetBytes(mask).Reverse().ToArray());
    }

    private static bool IsUsableAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
            return false;

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && !(bytes[0] == 169 && bytes[1] == 254);
    }

    private static bool IsProbablyVirtual(NetworkInterface networkInterface)
    {
        var descriptor = $"{networkInterface.Name} {networkInterface.Description}";
        return VirtualAdapterKeywords.Any(keyword => descriptor.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static int GetPriority(NetworkInterface networkInterface, bool hasGateway)
    {
        var typePriority = networkInterface.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => 400,
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => 350,
            NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT => 325,
            _ => 200
        };

        return typePriority + (hasGateway ? 1000 : 0);
    }
}
