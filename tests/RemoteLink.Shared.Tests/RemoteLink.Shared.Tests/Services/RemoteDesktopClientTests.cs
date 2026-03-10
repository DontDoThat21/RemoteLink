using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Shared.Tests.Services;

public class RemoteDesktopClientTests
{
    private sealed class FakeNetworkDiscovery : INetworkDiscovery
    {
        public event EventHandler<DeviceInfo>? DeviceDiscovered;
        public event EventHandler<DeviceInfo>? DeviceLost;

        public Task StartBroadcastingAsync() => Task.CompletedTask;
        public Task StopBroadcastingAsync() => Task.CompletedTask;
        public Task StartListeningAsync() => Task.CompletedTask;
        public Task StopListeningAsync() => Task.CompletedTask;
        public Task<IEnumerable<DeviceInfo>> GetDiscoveredDevicesAsync() => Task.FromResult(Enumerable.Empty<DeviceInfo>());
    }

    private sealed class FakeCommunicationService : ICommunicationService
    {
        public bool IsConnected { get; private set; }

        public event EventHandler<ScreenData>? ScreenDataReceived;
        public event EventHandler<InputEvent>? InputEventReceived;
        public event EventHandler<bool>? ConnectionStateChanged;
        public event EventHandler<PairingRequest>? PairingRequestReceived;
        public event EventHandler<PairingResponse>? PairingResponseReceived;
        public event EventHandler<ConnectionQuality>? ConnectionQualityReceived;
        public event EventHandler<SessionControlRequest>? SessionControlRequestReceived;
        public event EventHandler<SessionControlResponse>? SessionControlResponseReceived;
        public event EventHandler<ClipboardData>? ClipboardDataReceived;
        public event EventHandler<FileTransferRequest>? FileTransferRequestReceived;
        public event EventHandler<FileTransferResponse>? FileTransferResponseReceived;
        public event EventHandler<FileTransferChunk>? FileTransferChunkReceived;
        public event EventHandler<FileTransferComplete>? FileTransferCompleteReceived;
        public event EventHandler<AudioData>? AudioDataReceived;
        public event EventHandler<ChatMessage>? ChatMessageReceived;
        public event EventHandler<string>? MessageReadReceived;
        public event EventHandler<PrintJob>? PrintJobReceived;
        public event EventHandler<PrintJobResponse>? PrintJobResponseReceived;
        public event EventHandler<PrintJobStatus>? PrintJobStatusReceived;

        public Task StartAsync(int port) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;

        public Task<bool> ConnectToDeviceAsync(DeviceInfo device)
        {
            IsConnected = true;
            ConnectionStateChanged?.Invoke(this, true);
            return Task.FromResult(true);
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            ConnectionStateChanged?.Invoke(this, false);
            return Task.CompletedTask;
        }

        public Task SendScreenDataAsync(ScreenData screenData) => Task.CompletedTask;
        public Task SendInputEventAsync(InputEvent inputEvent) => Task.CompletedTask;

        public Task SendPairingRequestAsync(PairingRequest request)
        {
            PairingResponseReceived?.Invoke(this, new PairingResponse
            {
                Success = true,
                SessionToken = "session-token"
            });
            return Task.CompletedTask;
        }

        public Task SendPairingResponseAsync(PairingResponse response) => Task.CompletedTask;
        public Task SendConnectionQualityAsync(ConnectionQuality quality) => Task.CompletedTask;
        public Task SendSessionControlRequestAsync(SessionControlRequest request) => Task.CompletedTask;
        public Task SendSessionControlResponseAsync(SessionControlResponse response) => Task.CompletedTask;
        public Task SendClipboardDataAsync(ClipboardData clipboardData) => Task.CompletedTask;
        public Task SendFileTransferRequestAsync(FileTransferRequest request) => Task.CompletedTask;
        public Task SendFileTransferResponseAsync(FileTransferResponse response) => Task.CompletedTask;
        public Task SendFileTransferChunkAsync(FileTransferChunk chunk) => Task.CompletedTask;
        public Task SendFileTransferCompleteAsync(FileTransferComplete complete) => Task.CompletedTask;
        public Task SendAudioDataAsync(AudioData audioData) => Task.CompletedTask;
        public Task SendChatMessageAsync(ChatMessage message) => Task.CompletedTask;
        public Task SendMessageReadAsync(string messageId) => Task.CompletedTask;
        public Task SendPrintJobAsync(PrintJob printJob) => Task.CompletedTask;
        public Task SendPrintJobResponseAsync(PrintJobResponse response) => Task.CompletedTask;
        public Task SendPrintJobStatusAsync(PrintJobStatus status) => Task.CompletedTask;

        public void RaiseConnectionQuality(ConnectionQuality quality)
        {
            ConnectionQualityReceived?.Invoke(this, quality);
        }
    }

    [Fact]
    public async Task ConnectToHostAsync_WhenQualityReceived_UpdatesCurrentConnectionQuality()
    {
        var discovery = new FakeNetworkDiscovery();
        var comm = new FakeCommunicationService();
        var client = new RemoteDesktopClient(NullLogger<RemoteDesktopClient>.Instance, discovery, () => comm);

        var host = new DeviceInfo
        {
            DeviceId = "host-1",
            DeviceName = "Host",
            IPAddress = "127.0.0.1",
            Port = 12346,
            Type = DeviceType.Desktop
        };

        var updatedTcs = new TaskCompletionSource<ConnectionQuality>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ConnectionQualityUpdated += (_, quality) => updatedTcs.TrySetResult(quality);

        var connected = await client.ConnectToHostAsync(host, "123456");
        Assert.True(connected);

        var quality = new ConnectionQuality
        {
            Fps = 22,
            Bandwidth = 1_750_000,
            Latency = 82,
            Rating = QualityRating.Good,
            Timestamp = DateTime.UtcNow
        };

        comm.RaiseConnectionQuality(quality);

        var received = await updatedTcs.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(QualityRating.Good, received.Rating);
        Assert.NotNull(client.CurrentConnectionQuality);
        Assert.Equal(22, client.CurrentConnectionQuality!.Fps);
        Assert.Equal(82, client.CurrentConnectionQuality.Latency);
        Assert.Equal(1_750_000, client.CurrentConnectionQuality.Bandwidth);
    }

    [Fact]
    public async Task DisconnectAsync_ClearsCurrentConnectionQuality()
    {
        var discovery = new FakeNetworkDiscovery();
        var comm = new FakeCommunicationService();
        var client = new RemoteDesktopClient(NullLogger<RemoteDesktopClient>.Instance, discovery, () => comm);

        var host = new DeviceInfo
        {
            DeviceId = "host-1",
            DeviceName = "Host",
            IPAddress = "127.0.0.1",
            Port = 12346,
            Type = DeviceType.Desktop
        };

        var connected = await client.ConnectToHostAsync(host, "123456");
        Assert.True(connected);

        comm.RaiseConnectionQuality(new ConnectionQuality
        {
            Fps = 14,
            Bandwidth = 512_000,
            Latency = 180,
            Rating = QualityRating.Fair,
            Timestamp = DateTime.UtcNow
        });

        Assert.NotNull(client.CurrentConnectionQuality);

        await client.DisconnectAsync();

        Assert.Null(client.CurrentConnectionQuality);
        Assert.Equal(ClientConnectionState.Disconnected, client.ConnectionState);
    }
}
