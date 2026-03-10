using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Shared.Tests.Services;

public class SignalingTransportTests
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
    public async Task SignalingService_CanRegisterAndResolveDeviceByInternetDeviceId()
    {
        await using var signalingServer = new SignalingServer();
        await signalingServer.StartAsync(0);

        var configuration = new SignalingConfiguration
        {
            Enabled = true,
            ServerHost = "127.0.0.1",
            ServerPort = signalingServer.Port,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            RefreshInterval = TimeSpan.FromSeconds(10)
        };

        var hostDevice = new DeviceInfo
        {
            DeviceId = "signal-host-1",
            DeviceName = "Signal Host",
            IPAddress = "127.0.0.1",
            Port = 12346,
            Type = DeviceType.Desktop,
            SupportsRelay = true,
            RelayServerHost = "relay.example.test",
            RelayServerPort = 12400
        };

        using var registrar = new SignalingService(configuration);
        using var resolver = new SignalingService(configuration);

        await registrar.StartAsync(hostDevice);
        Assert.Matches("^[0-9]{9}$", hostDevice.InternetDeviceId ?? string.Empty);

        var resolved = await resolver.ResolveDeviceAsync(hostDevice.InternetDeviceId!);

        Assert.NotNull(resolved);
        Assert.Equal(hostDevice.DeviceId, resolved!.DeviceId);
        Assert.Equal(hostDevice.Port, resolved.Port);
        Assert.Equal(hostDevice.RelayServerHost, resolved.RelayServerHost);
        Assert.Equal(hostDevice.RelayServerPort, resolved.RelayServerPort);
        Assert.Equal("127.0.0.1", resolved.IPAddress);
        Assert.True(resolved.IsOnline);
    }

    [Fact]
    public async Task AdaptiveCommunicationService_CanResolveInternetOnlyTargetThroughSignalingDirectory()
    {
        await using var signalingServer = new SignalingServer();
        await signalingServer.StartAsync(0);

        var signalingConfiguration = new SignalingConfiguration
        {
            Enabled = true,
            ServerHost = "127.0.0.1",
            ServerPort = signalingServer.Port,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            RefreshInterval = TimeSpan.FromSeconds(10)
        };

        var hostDevice = new DeviceInfo
        {
            DeviceId = "adaptive-signal-host",
            DeviceName = "Adaptive Signal Host",
            Port = 12346,
            Type = DeviceType.Desktop
        };

        using var hostSignaling = new SignalingService(signalingConfiguration);
        using var clientSignaling = new SignalingService(signalingConfiguration);
        using var host = new AdaptiveCommunicationService(new NoOpNatTraversalService(), hostDevice, null, hostSignaling);
        using var client = new AdaptiveCommunicationService(new NoOpNatTraversalService(), null, null, clientSignaling);

        await host.StartAsync(hostDevice.Port);
        Assert.Matches("^[0-9]{9}$", hostDevice.InternetDeviceId ?? string.Empty);

        var pairingTcs = new TaskCompletionSource<PairingRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.PairingRequestReceived += (_, request) => pairingTcs.TrySetResult(request);

        var internetOnlyTarget = new DeviceInfo
        {
            DeviceId = hostDevice.InternetDeviceId!,
            InternetDeviceId = hostDevice.InternetDeviceId,
            DeviceName = DeviceIdentityManager.FormatInternetDeviceId(hostDevice.InternetDeviceId),
            Type = DeviceType.Desktop
        };

        var connected = await client.ConnectToDeviceAsync(internetOnlyTarget);
        Assert.True(connected);

        await client.SendPairingRequestAsync(new PairingRequest
        {
            ClientDeviceId = "signal-client-1",
            ClientDeviceName = "Signal Client",
            Pin = "999999",
            RequestedAt = DateTime.UtcNow
        });

        var request = await pairingTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("signal-client-1", request.ClientDeviceId);
        Assert.Equal("999999", request.Pin);
    }
}
