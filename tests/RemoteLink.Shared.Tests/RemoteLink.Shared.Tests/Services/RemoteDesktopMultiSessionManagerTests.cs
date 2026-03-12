using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Shared.Tests.Services;

public class RemoteDesktopMultiSessionManagerTests
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
        public PairingResponse PairingResponseToSend { get; set; } = new() { Success = true, SessionToken = Guid.NewGuid().ToString("N") };

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

        public Task SendPairingRequestAsync(PairingRequest request)
        {
            PairingResponseReceived?.Invoke(this, PairingResponseToSend);
            return Task.CompletedTask;
        }

        public void RaiseConnectionStateChanged(bool connected)
        {
            IsConnected = connected;
            ConnectionStateChanged?.Invoke(this, connected);
        }

        public Task SendScreenDataAsync(ScreenData screenData) => Task.CompletedTask;
        public Task SendInputEventAsync(InputEvent inputEvent) => Task.CompletedTask;
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
    }

    [Fact]
    public async Task ConnectAsync_AllowsMultipleSimultaneousSessions()
    {
        var discovery = new FakeNetworkDiscovery();
        var communications = new Queue<FakeCommunicationService>(
        [
            new FakeCommunicationService(),
            new FakeCommunicationService()
        ]);

        var manager = new RemoteDesktopMultiSessionManager(
            () => CreateClient(discovery, communications.Dequeue()),
            NullLogger<RemoteDesktopMultiSessionManager>.Instance);

        var session1 = await manager.ConnectAsync(CreateHost("host-1", "Alpha", 12346), "123456");
        var session2 = await manager.ConnectAsync(CreateHost("host-2", "Beta", 12347), "654321");

        var sessions = manager.GetSessions();

        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, session => session.SessionId == session1.SessionId && session.DisplayName == "Alpha");
        Assert.Contains(sessions, session => session.SessionId == session2.SessionId && session.DisplayName == "Beta");
        Assert.NotSame(session1.Client, session2.Client);
    }

    [Fact]
    public async Task ConnectAsync_WhenSessionAlreadyExistsForHost_ReusesExistingSession()
    {
        var discovery = new FakeNetworkDiscovery();
        var factoryCalls = 0;
        var manager = new RemoteDesktopMultiSessionManager(
            () =>
            {
                factoryCalls++;
                return CreateClient(discovery, new FakeCommunicationService());
            },
            NullLogger<RemoteDesktopMultiSessionManager>.Instance);

        var host = CreateHost("host-1", "Alpha", 12346);
        var firstSession = await manager.ConnectAsync(host, "123456");
        var secondSession = await manager.ConnectAsync(host, "123456");

        Assert.Equal(firstSession.SessionId, secondSession.SessionId);
        Assert.Single(manager.GetSessions());
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task CloseSessionAsync_RemovesOnlyRequestedSession()
    {
        var discovery = new FakeNetworkDiscovery();
        var communications = new Queue<FakeCommunicationService>(
        [
            new FakeCommunicationService(),
            new FakeCommunicationService()
        ]);

        var manager = new RemoteDesktopMultiSessionManager(
            () => CreateClient(discovery, communications.Dequeue()),
            NullLogger<RemoteDesktopMultiSessionManager>.Instance);

        var session1 = await manager.ConnectAsync(CreateHost("host-1", "Alpha", 12346), "123456");
        var session2 = await manager.ConnectAsync(CreateHost("host-2", "Beta", 12347), "654321");

        var removed = await manager.CloseSessionAsync(session1.SessionId);
        var remainingSessions = manager.GetSessions();

        Assert.True(removed);
        Assert.Single(remainingSessions);
        Assert.Equal(session2.SessionId, remainingSessions[0].SessionId);
    }

    private static RemoteDesktopClient CreateClient(FakeNetworkDiscovery discovery, FakeCommunicationService communicationService)
        => new(
            NullLogger<RemoteDesktopClient>.Instance,
            discovery,
            () => communicationService);

    private static DeviceInfo CreateHost(string deviceId, string deviceName, int port)
        => new()
        {
            DeviceId = deviceId,
            DeviceName = deviceName,
            IPAddress = "127.0.0.1",
            Port = port,
            Type = DeviceType.Desktop
        };
}
