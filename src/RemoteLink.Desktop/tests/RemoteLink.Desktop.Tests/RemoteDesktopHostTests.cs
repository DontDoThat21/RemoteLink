using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Desktop.Services;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Tests;

// ── Test Doubles ──────────────────────────────────────────────────────────────
// Hand-rolled fakes (no mocking framework needed).

/// <summary>In-memory fake for <see cref="ICommunicationService"/> used in host tests.</summary>
internal sealed class FakeCommunicationService : ICommunicationService, IDisposable
{
    // Captured outbound messages
    public List<ScreenData> SentScreenData { get; } = new();
    public List<InputEvent> SentInputEvents { get; } = new();
    public List<PairingResponse> SentPairingResponses { get; } = new();

    // Settable connection state — tests can toggle this
    public bool IsConnected { get; set; }

    // ── ICommunicationService events ──────────────────────────────────────────
    public event EventHandler<ScreenData>? ScreenDataReceived;
    public event EventHandler<InputEvent>? InputEventReceived;
    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<PairingRequest>? PairingRequestReceived;
    public event EventHandler<PairingResponse>? PairingResponseReceived;

    // ── ICommunicationService methods ─────────────────────────────────────────
    public Task StartAsync(int port) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task<bool> ConnectToDeviceAsync(DeviceInfo device) => Task.FromResult(true);
    public Task DisconnectAsync() => Task.CompletedTask;

    public Task SendScreenDataAsync(ScreenData screenData)
    {
        SentScreenData.Add(screenData);
        return Task.CompletedTask;
    }

    public Task SendInputEventAsync(InputEvent inputEvent)
    {
        SentInputEvents.Add(inputEvent);
        return Task.CompletedTask;
    }

    public Task SendPairingRequestAsync(PairingRequest request) => Task.CompletedTask;

    public Task SendPairingResponseAsync(PairingResponse response)
    {
        SentPairingResponses.Add(response);
        return Task.CompletedTask;
    }

    // ── Test helpers — raise events on behalf of a remote client ─────────────

    /// <summary>Simulate a client connecting or disconnecting.</summary>
    public void RaiseConnectionStateChanged(bool connected)
    {
        IsConnected = connected;
        ConnectionStateChanged?.Invoke(this, connected);
    }

    /// <summary>Simulate a pairing request from a remote client.</summary>
    public void RaisePairingRequest(PairingRequest request)
        => PairingRequestReceived?.Invoke(this, request);

    /// <summary>Simulate an input event from a remote client.</summary>
    public void RaiseInputEventReceived(InputEvent inputEvent)
        => InputEventReceived?.Invoke(this, inputEvent);

    public void Dispose() { }
}

/// <summary>In-memory fake for <see cref="IScreenCapture"/>.</summary>
internal sealed class FakeScreenCapture : IScreenCapture
{
    public bool IsCapturing { get; private set; }
    public int StartCallCount { get; private set; }
    public int StopCallCount { get; private set; }

    public event EventHandler<ScreenData>? FrameCaptured;

    public Task StartCaptureAsync()
    {
        IsCapturing = true;
        StartCallCount++;
        return Task.CompletedTask;
    }

    public Task StopCaptureAsync()
    {
        IsCapturing = false;
        StopCallCount++;
        return Task.CompletedTask;
    }

    public Task<ScreenData> CaptureFrameAsync()
        => Task.FromResult(new ScreenData { Width = 1, Height = 1 });

    public Task<(int Width, int Height)> GetScreenDimensionsAsync()
        => Task.FromResult((1920, 1080));

    public void SetQuality(int quality) { }

    /// <summary>Manually fire a <see cref="FrameCaptured"/> event.</summary>
    public void RaiseFrameCaptured(ScreenData data)
        => FrameCaptured?.Invoke(this, data);
}

/// <summary>In-memory fake for <see cref="IInputHandler"/>.</summary>
internal sealed class FakeInputHandler : IInputHandler
{
    public List<InputEvent> ReceivedEvents { get; } = new();
    public bool IsActive { get; private set; }

    public Task StartAsync()
    {
        IsActive = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsActive = false;
        return Task.CompletedTask;
    }

    public Task ProcessInputEventAsync(InputEvent inputEvent)
    {
        ReceivedEvents.Add(inputEvent);
        return Task.CompletedTask;
    }
}

/// <summary>In-memory fake for <see cref="IPairingService"/>.</summary>
internal sealed class FakePairingService : IPairingService
{
    private string _currentPin = "123456";

    /// <summary>Controls whether <see cref="ValidatePin"/> returns true.</summary>
    public bool ValidatePinResult { get; set; } = true;

    public bool IsLockedOut { get; set; }
    public string? CurrentPin => _currentPin;
    public bool IsPinExpired => false;
    public int AttemptsRemaining => IsLockedOut ? 0 : 5;
    public int MaxAttempts => 5;

    public event EventHandler<string>? PinGenerated;
    public event EventHandler<PairingAttemptResult>? PairingAttempted;

    public string GeneratePin()
    {
        _currentPin = "123456";
        PinGenerated?.Invoke(this, _currentPin);
        return _currentPin;
    }

    public void RefreshPin() => GeneratePin();

    public bool ValidatePin(string pin)
    {
        bool result = ValidatePinResult && !IsLockedOut;
        PairingAttempted?.Invoke(this, new PairingAttemptResult { Success = result });
        return result;
    }
}

/// <summary>In-memory fake for <see cref="INetworkDiscovery"/>.</summary>
internal sealed class FakeNetworkDiscovery : INetworkDiscovery
{
    public event EventHandler<DeviceInfo>? DeviceDiscovered;
    public event EventHandler<DeviceInfo>? DeviceLost;

    public Task StartBroadcastingAsync() => Task.CompletedTask;
    public Task StopBroadcastingAsync() => Task.CompletedTask;
    public Task StartListeningAsync() => Task.CompletedTask;
    public Task StopListeningAsync() => Task.CompletedTask;

    public Task<IEnumerable<DeviceInfo>> GetDiscoveredDevicesAsync()
        => Task.FromResult(Enumerable.Empty<DeviceInfo>());
}

// ── Tests ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Tests for <see cref="RemoteDesktopHost"/> covering:
/// <list type="bullet">
///   <item><description>2.3 — Screen streaming (host → client): capture gating, frame forwarding, stop-on-disconnect.</description></item>
///   <item><description>2.5 — Remote input relay (client → host): relay when paired, ignore when not.</description></item>
/// </list>
/// </summary>
public class RemoteDesktopHostTests : IAsyncDisposable
{
    private readonly FakeCommunicationService _comm = new();
    private readonly FakeScreenCapture _screen = new();
    private readonly FakeInputHandler _input = new();
    private readonly FakePairingService _pairing = new();
    private readonly FakeNetworkDiscovery _discovery = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly RemoteDesktopHost _host;
    private Task? _hostTask;

    public RemoteDesktopHostTests()
    {
        _host = new RemoteDesktopHost(
            NullLogger<RemoteDesktopHost>.Instance,
            _discovery,
            _screen,
            _input,
            _comm,
            _pairing);
    }

    /// <summary>Kick off the host as a BackgroundService and wait for it to initialize.</summary>
    private async Task StartHostAsync()
    {
        _hostTask = _host.StartAsync(_cts.Token);
        await Task.Delay(60); // let ExecuteAsync reach its idle loop
    }

    /// <summary>
    /// Full happy-path pairing sequence:
    /// 1. Connection state → connected
    /// 2. PairingRequest with the correct PIN
    /// 3. Wait for async Task.Run inside OnPairingRequestReceived to finish
    /// </summary>
    private async Task SimulatePairingAsync()
    {
        _comm.RaiseConnectionStateChanged(connected: true);
        await Task.Delay(20);

        _comm.RaisePairingRequest(new PairingRequest
        {
            ClientDeviceId = "test-client",
            ClientDeviceName = "TestDevice",
            Pin = _pairing.CurrentPin!,
            RequestedAt = DateTime.UtcNow
        });

        // Allow the Task.Run inside OnPairingRequestReceived to complete
        await Task.Delay(120);
    }

    // ── Feature 2.3: Screen streaming (host → client) ─────────────────────────

    [Fact]
    public async Task ScreenCapture_NotStarted_BeforeAnyClientConnects()
    {
        await StartHostAsync();

        Assert.Equal(0, _screen.StartCallCount);
        Assert.False(_screen.IsCapturing);
    }

    [Fact]
    public async Task ScreenCapture_NotStarted_WhenClientConnectsButBeforePairing()
    {
        await StartHostAsync();

        _comm.RaiseConnectionStateChanged(connected: true);
        await Task.Delay(60);

        // Streaming must NOT begin until the client successfully pairs
        Assert.Equal(0, _screen.StartCallCount);
    }

    [Fact]
    public async Task ScreenCapture_StartsAfterSuccessfulPairing()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        Assert.Equal(1, _screen.StartCallCount);
        Assert.True(_screen.IsCapturing);
    }

    [Fact]
    public async Task ScreenCapture_NotStarted_WhenPairingFails()
    {
        _pairing.ValidatePinResult = false;

        await StartHostAsync();
        _comm.RaiseConnectionStateChanged(connected: true);
        _comm.RaisePairingRequest(new PairingRequest
        {
            ClientDeviceId = "bad-client",
            Pin = "000000",
            RequestedAt = DateTime.UtcNow
        });
        await Task.Delay(120);

        Assert.Equal(0, _screen.StartCallCount);
    }

    [Fact]
    public async Task Frame_IsSent_ToClient_AfterSuccessfulPairing()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        var frame = new ScreenData
        {
            Width = 1920,
            Height = 1080,
            Format = ScreenDataFormat.Raw,
            ImageData = new byte[] { 10, 20, 30 }
        };

        _screen.RaiseFrameCaptured(frame);
        await Task.Delay(120); // let the async Task.Run in OnFrameCaptured complete

        Assert.Single(_comm.SentScreenData);
        Assert.Equal(1920, _comm.SentScreenData[0].Width);
        Assert.Equal(1080, _comm.SentScreenData[0].Height);
        Assert.Equal(new byte[] { 10, 20, 30 }, _comm.SentScreenData[0].ImageData);
    }

    [Fact]
    public async Task Frame_NotSent_WhenClientHasNotPaired()
    {
        // Host started, but client never connects or pairs — FrameCaptured handler is never attached
        await StartHostAsync();

        _screen.RaiseFrameCaptured(new ScreenData { Width = 800, Height = 600 });
        await Task.Delay(120);

        Assert.Empty(_comm.SentScreenData);
    }

    [Fact]
    public async Task Frame_NotSent_WhenIsConnectedIsFalse_EvenIfPaired()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        // Simulate the network connection dropping without a state-changed event
        _comm.IsConnected = false;

        _screen.RaiseFrameCaptured(new ScreenData { Width = 800, Height = 600 });
        await Task.Delay(120);

        Assert.Empty(_comm.SentScreenData);
    }

    [Fact]
    public async Task MultipleFrames_AllSent_ToClient()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        for (int i = 1; i <= 5; i++)
        {
            _screen.RaiseFrameCaptured(new ScreenData
            {
                Width = i * 100,
                Height = i * 75,
                ImageData = new byte[] { (byte)i }
            });
        }
        await Task.Delay(300); // allow all 5 async Task.Run sends to land

        // All 5 frames must be delivered; concurrent Task.Run sends don't
        // guarantee insertion order, so check presence rather than sequence.
        Assert.Equal(5, _comm.SentScreenData.Count);
        var widths = _comm.SentScreenData.Select(s => s.Width).ToHashSet();
        for (int i = 1; i <= 5; i++)
            Assert.Contains(i * 100, widths);
    }

    [Fact]
    public async Task ScreenCapture_StopsWhenClientDisconnects()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        Assert.Equal(1, _screen.StartCallCount);

        // Simulate disconnect
        _comm.RaiseConnectionStateChanged(connected: false);
        await Task.Delay(60);

        Assert.Equal(1, _screen.StopCallCount);
        Assert.False(_screen.IsCapturing);
    }

    [Fact]
    public async Task Frame_NotSent_AfterClientDisconnects()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        _comm.RaiseConnectionStateChanged(connected: false);
        await Task.Delay(60);

        // Any frames that fire after disconnect should be silently dropped
        _screen.RaiseFrameCaptured(new ScreenData { Width = 1280, Height = 720 });
        await Task.Delay(120);

        Assert.Empty(_comm.SentScreenData);
    }

    // ── Pairing response assertions ───────────────────────────────────────────

    [Fact]
    public async Task PairingResponse_Sent_WithSuccess_OnValidPin()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        Assert.Single(_comm.SentPairingResponses);
        var response = _comm.SentPairingResponses[0];
        Assert.True(response.Success);
        Assert.NotNull(response.SessionToken);
        Assert.False(string.IsNullOrEmpty(response.SessionToken));
    }

    [Fact]
    public async Task PairingResponse_Sent_WithInvalidPin_OnBadPin()
    {
        _pairing.ValidatePinResult = false;

        await StartHostAsync();
        _comm.RaiseConnectionStateChanged(connected: true);
        _comm.RaisePairingRequest(new PairingRequest
        {
            ClientDeviceId = "bad",
            Pin = "000000",
            RequestedAt = DateTime.UtcNow
        });
        await Task.Delay(120);

        Assert.Single(_comm.SentPairingResponses);
        var response = _comm.SentPairingResponses[0];
        Assert.False(response.Success);
        Assert.Equal(PairingFailureReason.InvalidPin, response.FailureReason);
    }

    [Fact]
    public async Task PairingResponse_Sent_WithTooManyAttempts_WhenLockedOut()
    {
        _pairing.ValidatePinResult = false;
        _pairing.IsLockedOut = true;

        await StartHostAsync();
        _comm.RaiseConnectionStateChanged(connected: true);
        _comm.RaisePairingRequest(new PairingRequest
        {
            ClientDeviceId = "bad",
            Pin = "000000",
            RequestedAt = DateTime.UtcNow
        });
        await Task.Delay(120);

        Assert.Single(_comm.SentPairingResponses);
        var response = _comm.SentPairingResponses[0];
        Assert.False(response.Success);
        Assert.Equal(PairingFailureReason.TooManyAttempts, response.FailureReason);
    }

    // ── Feature 2.5: Remote input relay (client → host) ───────────────────────

    [Fact]
    public async Task InputEvent_Relayed_ToInputHandler_WhenPaired()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        var evt = new InputEvent
        {
            Type = InputEventType.MouseClick,
            X = 200,
            Y = 300,
            IsPressed = true
        };

        _comm.RaiseInputEventReceived(evt);
        await Task.Delay(120);

        Assert.Single(_input.ReceivedEvents);
        Assert.Equal(InputEventType.MouseClick, _input.ReceivedEvents[0].Type);
        Assert.Equal(200, _input.ReceivedEvents[0].X);
        Assert.Equal(300, _input.ReceivedEvents[0].Y);
        Assert.True(_input.ReceivedEvents[0].IsPressed);
    }

    [Fact]
    public async Task InputEvent_NotRelayed_WhenClientHasNotPaired()
    {
        await StartHostAsync();
        // No pairing — _clientPaired remains false

        _comm.RaiseInputEventReceived(new InputEvent { Type = InputEventType.KeyPress, KeyCode = "A" });
        await Task.Delay(120);

        Assert.Empty(_input.ReceivedEvents);
    }

    [Fact]
    public async Task MultipleInputEvents_AllRelayed_WhenPaired()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        var events = new[]
        {
            new InputEvent { Type = InputEventType.MouseMove,  X = 10, Y = 20 },
            new InputEvent { Type = InputEventType.MouseClick, X = 50, Y = 60, IsPressed = true },
            new InputEvent { Type = InputEventType.KeyPress,   KeyCode = "Space" },
            new InputEvent { Type = InputEventType.MouseWheel, Y = 3 },
            new InputEvent { Type = InputEventType.TextInput,  Text = "hello" }
        };

        foreach (var e in events)
            _comm.RaiseInputEventReceived(e);

        await Task.Delay(250);

        Assert.Equal(5, _input.ReceivedEvents.Count);
    }

    [Fact]
    public async Task InputEvent_NotRelayed_AfterClientDisconnects()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        // Disconnect — resets _clientPaired to false
        _comm.RaiseConnectionStateChanged(connected: false);
        await Task.Delay(60);

        _comm.RaiseInputEventReceived(new InputEvent { Type = InputEventType.KeyPress, KeyCode = "A" });
        await Task.Delay(120);

        // No events should have been relayed after disconnect
        Assert.Empty(_input.ReceivedEvents);
    }

    [Fact]
    public async Task AllInputEventTypes_Relayed_WhenPaired()
    {
        await StartHostAsync();
        await SimulatePairingAsync();

        var allTypes = new[]
        {
            InputEventType.MouseMove,
            InputEventType.MouseClick,
            InputEventType.MouseWheel,
            InputEventType.KeyPress,
            InputEventType.KeyRelease,
            InputEventType.TextInput
        };

        foreach (var type in allTypes)
            _comm.RaiseInputEventReceived(new InputEvent { Type = type });

        await Task.Delay(300);

        Assert.Equal(allTypes.Length, _input.ReceivedEvents.Count);

        for (int i = 0; i < allTypes.Length; i++)
            Assert.Equal(allTypes[i], _input.ReceivedEvents[i].Type);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task InputHandler_IsStarted_WhenHostStarts()
    {
        await StartHostAsync();
        Assert.True(_input.IsActive);
    }

    [Fact]
    public async Task InputHandler_IsStopped_WhenHostStops()
    {
        await StartHostAsync();
        Assert.True(_input.IsActive);

        // Cancel the hosted service's lifetime token, then call StopAsync so the
        // test waits for ExecuteAsync's finally-block (which calls StopAsync on
        // all dependencies) before asserting.  BackgroundService.StartAsync returns
        // Task.CompletedTask when ExecuteAsync is long-running, so we cannot rely
        // on _hostTask for shutdown synchronisation.
        _cts.Cancel();
        await _host.StopAsync(CancellationToken.None);

        Assert.False(_input.IsActive);
    }

    [Fact]
    public async Task Host_DoesNotThrow_OnCleanCancellation()
    {
        await StartHostAsync();

        _cts.Cancel();
        // StopAsync must complete without throwing
        var ex = await Record.ExceptionAsync(() => _host.StopAsync(CancellationToken.None));
        Assert.Null(ex);
    }

    // ── IAsyncDisposable ───────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (!_cts.IsCancellationRequested)
            _cts.Cancel();

        // StopAsync waits for ExecuteAsync to finish (including its finally block),
        // which is what we need to drain background Task.Run work before teardown.
        try { await _host.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)); }
        catch { /* ignore */ }

        _cts.Dispose();
        _comm.Dispose();
    }
}
