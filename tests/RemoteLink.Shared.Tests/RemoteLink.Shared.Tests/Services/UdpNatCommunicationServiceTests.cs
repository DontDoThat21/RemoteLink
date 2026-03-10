using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Shared.Tests.Services;

public class UdpNatCommunicationServiceTests
{
    private static NatTraversalOptions CreateOptions() => new()
    {
        StunServers = new List<string>(),
        PunchTimeout = TimeSpan.FromSeconds(2),
        PunchInterval = TimeSpan.FromMilliseconds(100)
    };

    [Fact]
    public async Task ConnectToDeviceAsync_AndSendPairingRequest_WorksOverUdpTransport()
    {
        using var hostNat = new NatTraversalService(options: CreateOptions());
        using var clientNat = new NatTraversalService(options: CreateOptions());
        using var host = new UdpNatCommunicationService(hostNat);
        using var client = new UdpNatCommunicationService(clientNat);

        await host.StartAsync(0);
        var hostDiscovery = hostNat.CurrentDiscovery!;

        var hostDevice = new DeviceInfo
        {
            DeviceId = "host-1",
            DeviceName = "Host",
            IPAddress = "127.0.0.1",
            Port = hostDiscovery.LocalPort,
            PublicIPAddress = hostDiscovery.PublicIPAddress,
            PublicPort = hostDiscovery.PublicPort,
            NatCandidates = hostDiscovery.Candidates,
            Type = DeviceType.Desktop
        };

        var hostConnectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pairingTcs = new TaskCompletionSource<PairingRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

        host.ConnectionStateChanged += (_, connected) =>
        {
            if (connected)
                hostConnectedTcs.TrySetResult(true);
        };
        host.PairingRequestReceived += (_, request) => pairingTcs.TrySetResult(request);

        var connected = await client.ConnectToDeviceAsync(hostDevice);

        Assert.True(connected);
        Assert.True(await hostConnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2)));

        await client.SendPairingRequestAsync(new PairingRequest
        {
            ClientDeviceId = "client-1",
            ClientDeviceName = "Client",
            Pin = "123456",
            RequestedAt = DateTime.UtcNow
        });

        var pairing = await pairingTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("client-1", pairing.ClientDeviceId);
        Assert.Equal("123456", pairing.Pin);
    }

    [Fact]
    public async Task SendScreenDataAsync_WithLargePayload_ReassemblesOnReceiver()
    {
        using var hostNat = new NatTraversalService(options: CreateOptions());
        using var clientNat = new NatTraversalService(options: CreateOptions());
        using var host = new UdpNatCommunicationService(hostNat);
        using var client = new UdpNatCommunicationService(clientNat);

        await host.StartAsync(0);
        var hostDiscovery = hostNat.CurrentDiscovery!;

        var hostDevice = new DeviceInfo
        {
            DeviceId = "host-1",
            DeviceName = "Host",
            IPAddress = "127.0.0.1",
            Port = hostDiscovery.LocalPort,
            PublicIPAddress = hostDiscovery.PublicIPAddress,
            PublicPort = hostDiscovery.PublicPort,
            NatCandidates = hostDiscovery.Candidates,
            Type = DeviceType.Desktop
        };

        var hostConnectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ConnectionStateChanged += (_, connected) =>
        {
            if (connected)
                hostConnectedTcs.TrySetResult(true);
        };

        var screenTcs = new TaskCompletionSource<ScreenData>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ScreenDataReceived += (_, screenData) => screenTcs.TrySetResult(screenData);

        Assert.True(await client.ConnectToDeviceAsync(hostDevice));
        Assert.True(await hostConnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2)));

        var payload = Enumerable.Range(0, 150_000).Select(i => (byte)(i % 251)).ToArray();
        await host.SendScreenDataAsync(new ScreenData
        {
            FrameId = "frame-1",
            Width = 1920,
            Height = 1080,
            Format = ScreenDataFormat.Raw,
            ImageData = payload,
            Timestamp = DateTime.UtcNow
        });

        var received = await screenTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal("frame-1", received.FrameId);
        Assert.Equal(1920, received.Width);
        Assert.Equal(1080, received.Height);
        Assert.Equal(payload.Length, received.ImageData.Length);
        Assert.Equal(payload, received.ImageData);
    }
}
