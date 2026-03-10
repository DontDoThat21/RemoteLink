using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Shared.Tests.Services;

public class RelayTransportTests
{
    private sealed class NoOpNatTraversalService : INatTraversalService
    {
        public bool IsRunning { get; private set; }
        public NatDiscoveryResult? CurrentDiscovery { get; private set; }
        public event EventHandler<NatDatagramReceivedEventArgs>? DatagramReceived;

        public Task<NatDiscoveryResult> StartAsync(int localPort, CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            CurrentDiscovery = new NatDiscoveryResult { LocalPort = localPort };
            return Task.FromResult(CurrentDiscovery);
        }

        public Task<NatDiscoveryResult> RefreshCandidatesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CurrentDiscovery ?? new NatDiscoveryResult());

        public Task StopAsync()
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public Task<NatTraversalConnectResult> TryConnectAsync(IEnumerable<NatEndpointCandidate> remoteCandidates, CancellationToken cancellationToken = default)
            => Task.FromResult(new NatTraversalConnectResult { Success = false, FailureReason = "disabled" });

        public Task SendDatagramAsync(string remoteIPAddress, int remotePort, byte[] payload, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task RelayCommunicationService_CanBridgePairingRequestThroughRelayServer()
    {
        await using var relayServer = new RelayServer();
        await relayServer.StartAsync(0);

        var relayConfiguration = new RelayConfiguration
        {
            Enabled = true,
            ServerHost = "127.0.0.1",
            ServerPort = relayServer.Port,
            ConnectTimeout = TimeSpan.FromSeconds(3)
        };

        var hostDevice = new DeviceInfo
        {
            DeviceId = "host-1",
            DeviceName = "Host",
            IPAddress = "127.0.0.1",
            Port = 12346,
            Type = DeviceType.Desktop
        };
        relayConfiguration.ApplyTo(hostDevice);

        var clientDevice = new DeviceInfo
        {
            DeviceId = "client-1",
            DeviceName = "Client",
            IPAddress = "127.0.0.1",
            Port = 12347,
            Type = DeviceType.Mobile
        };
        relayConfiguration.ApplyTo(clientDevice);

        using var host = new RelayCommunicationService(hostDevice, relayConfiguration);
        using var client = new RelayCommunicationService(clientDevice, relayConfiguration);

        await host.StartAsync(hostDevice.Port);

        var pairingTcs = new TaskCompletionSource<PairingRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.PairingRequestReceived += (_, request) => pairingTcs.TrySetResult(request);

        var connected = await client.ConnectToDeviceAsync(hostDevice);
        Assert.True(connected);

        await client.SendPairingRequestAsync(new PairingRequest
        {
            ClientDeviceId = clientDevice.DeviceId,
            ClientDeviceName = clientDevice.DeviceName,
            Pin = "123456",
            RequestedAt = DateTime.UtcNow
        });

        var request = await pairingTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("client-1", request.ClientDeviceId);
        Assert.Equal("123456", request.Pin);
    }

    [Fact]
    public async Task AdaptiveCommunicationService_FallsBackToRelay_WhenDirectTransportFails()
    {
        await using var relayServer = new RelayServer();
        await relayServer.StartAsync(0);

        var relayConfiguration = new RelayConfiguration
        {
            Enabled = true,
            ServerHost = "127.0.0.1",
            ServerPort = relayServer.Port,
            ConnectTimeout = TimeSpan.FromSeconds(3)
        };

        var hostDevice = new DeviceInfo
        {
            DeviceId = "host-relay",
            DeviceName = "Relay Host",
            IPAddress = "127.0.0.1",
            Port = 12346,
            Type = DeviceType.Desktop
        };
        relayConfiguration.ApplyTo(hostDevice);

        var clientDevice = new DeviceInfo
        {
            DeviceId = "client-relay",
            DeviceName = "Relay Client",
            IPAddress = "127.0.0.1",
            Port = 12347,
            Type = DeviceType.Mobile
        };
        relayConfiguration.ApplyTo(clientDevice);

        using var host = new AdaptiveCommunicationService(new NoOpNatTraversalService(), hostDevice, relayConfiguration);
        using var client = new AdaptiveCommunicationService(new NoOpNatTraversalService(), clientDevice, relayConfiguration);

        await host.StartAsync(hostDevice.Port);

        var pairingTcs = new TaskCompletionSource<PairingRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.PairingRequestReceived += (_, request) => pairingTcs.TrySetResult(request);

        var advertisedHost = new DeviceInfo
        {
            DeviceId = hostDevice.DeviceId,
            DeviceName = hostDevice.DeviceName,
            IPAddress = "127.0.0.1",
            Port = 9,
            Type = DeviceType.Desktop,
            SupportsRelay = true,
            RelayServerHost = relayConfiguration.ServerHost,
            RelayServerPort = relayConfiguration.ServerPort
        };

        var connected = await client.ConnectToDeviceAsync(advertisedHost);
        Assert.True(connected);

        await client.SendPairingRequestAsync(new PairingRequest
        {
            ClientDeviceId = clientDevice.DeviceId,
            ClientDeviceName = clientDevice.DeviceName,
            Pin = "654321",
            RequestedAt = DateTime.UtcNow
        });

        var request = await pairingTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("client-relay", request.ClientDeviceId);
        Assert.Equal("654321", request.Pin);
    }
}
