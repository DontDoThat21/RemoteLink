using System.Net;
using System.Net.Sockets;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;

namespace RemoteLink.Shared.Tests;

/// <summary>
/// Integration tests for TcpCommunicationService.
/// These tests spin up real TCP listeners on localhost to verify end-to-end
/// message exchange between a host (server) instance and a client instance.
/// </summary>
public class TcpCommunicationServiceTests : IAsyncDisposable
{
    // Find a free port at test startup to avoid port conflicts.
    private readonly int _port = GetFreePort();

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // ── IsConnected ──────────────────────────────────────────────────────────

    [Fact]
    public void IsConnected_Initially_False()
    {
        using var svc = new TcpCommunicationService();
        Assert.False(svc.IsConnected);
    }

    // ── StartAsync / StopAsync ───────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_ThenStopAsync_CompletesCleanly()
    {
        using var host = new TcpCommunicationService();
        await host.StartAsync(_port);
        await host.StopAsync();
        // No exception = pass
    }

    // ── ConnectToDeviceAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ConnectToDeviceAsync_ReturnsTrue_WhenServerIsListening()
    {
        using var server = new TcpCommunicationService();
        using var client = new TcpCommunicationService();

        await server.StartAsync(_port);

        var device = new DeviceInfo
        {
            DeviceId = "test-host",
            DeviceName = "TestHost",
            IPAddress = "127.0.0.1",
            Port = _port,
            Type = DeviceType.Desktop
        };

        bool result = await client.ConnectToDeviceAsync(device);

        await server.StopAsync();
        await client.StopAsync();

        Assert.True(result);
    }

    [Fact]
    public async Task ConnectToDeviceAsync_ReturnsFalse_WhenNoServerListening()
    {
        using var client = new TcpCommunicationService();

        var device = new DeviceInfo
        {
            DeviceId = "ghost",
            DeviceName = "Ghost",
            IPAddress = "127.0.0.1",
            Port = _port, // nothing listening here
            Type = DeviceType.Desktop
        };

        bool result = await client.ConnectToDeviceAsync(device);

        Assert.False(result);
    }

    [Fact]
    public async Task ConnectToDeviceAsync_ReturnsFalse_WhenDeviceHasNoIP()
    {
        using var client = new TcpCommunicationService();

        var device = new DeviceInfo
        {
            DeviceId = "nip",
            DeviceName = "NoIP",
            IPAddress = string.Empty,
            Port = _port,
            Type = DeviceType.Desktop
        };

        bool result = await client.ConnectToDeviceAsync(device);
        Assert.False(result);
    }

    // ── IsConnected after connect ────────────────────────────────────────────

    [Fact]
    public async Task IsConnected_True_AfterSuccessfulConnect()
    {
        using var server = new TcpCommunicationService();
        using var client = new TcpCommunicationService();

        await server.StartAsync(_port);

        var device = new DeviceInfo
        {
            DeviceId = "h", DeviceName = "H",
            IPAddress = "127.0.0.1", Port = _port,
            Type = DeviceType.Desktop
        };

        await client.ConnectToDeviceAsync(device);

        bool connected = client.IsConnected;

        await client.StopAsync();
        await server.StopAsync();

        Assert.True(connected);
    }

    // ── Screen data round-trip ───────────────────────────────────────────────

    [Fact]
    public async Task SendScreenDataAsync_IsReceived_ByConnectedPeer()
    {
        using var server = new TcpCommunicationService();
        using var client = new TcpCommunicationService();

        var tcs = new TaskCompletionSource<ScreenData>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Server will fire ScreenDataReceived when it gets data from the client.
        // Client will fire ScreenDataReceived when it gets data from the server.
        // We'll have the server send to the client, so register on the client side.
        client.ScreenDataReceived += (_, sd) => tcs.TrySetResult(sd);

        await server.StartAsync(_port);

        var device = new DeviceInfo
        {
            DeviceId = "h", DeviceName = "H",
            IPAddress = "127.0.0.1", Port = _port,
            Type = DeviceType.Desktop
        };
        await client.ConnectToDeviceAsync(device);

        // Give the accept loop a moment to pick up the connection
        await Task.Delay(100);

        var payload = new ScreenData
        {
            Width = 1920,
            Height = 1080,
            Format = ScreenDataFormat.JPEG,
            Quality = 80,
            ImageData = new byte[] { 1, 2, 3, 4, 5 }
        };

        await server.SendScreenDataAsync(payload);

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await client.StopAsync();
        await server.StopAsync();

        Assert.Equal(1920, received.Width);
        Assert.Equal(1080, received.Height);
        Assert.Equal(ScreenDataFormat.JPEG, received.Format);
        Assert.Equal(80, received.Quality);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, received.ImageData);
    }

    // ── Input event round-trip ───────────────────────────────────────────────

    [Fact]
    public async Task SendInputEventAsync_IsReceived_ByConnectedPeer()
    {
        using var server = new TcpCommunicationService();
        using var client = new TcpCommunicationService();

        var tcs = new TaskCompletionSource<InputEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Client sends input → server receives it
        server.InputEventReceived += (_, ie) => tcs.TrySetResult(ie);

        await server.StartAsync(_port);

        var device = new DeviceInfo
        {
            DeviceId = "h", DeviceName = "H",
            IPAddress = "127.0.0.1", Port = _port,
            Type = DeviceType.Desktop
        };
        await client.ConnectToDeviceAsync(device);

        await Task.Delay(100); // let accept loop run

        var inputEvent = new InputEvent
        {
            Type = InputEventType.MouseClick,
            X = 100,
            Y = 200,
            IsPressed = true
        };

        await client.SendInputEventAsync(inputEvent);

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await client.StopAsync();
        await server.StopAsync();

        Assert.Equal(InputEventType.MouseClick, received.Type);
        Assert.Equal(100, received.X);
        Assert.Equal(200, received.Y);
        Assert.True(received.IsPressed);
    }

    // ── ConnectionStateChanged ───────────────────────────────────────────────

    [Fact]
    public async Task ConnectionStateChanged_FiresTrue_WhenClientConnects()
    {
        using var server = new TcpCommunicationService();
        using var client = new TcpCommunicationService();

        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        server.ConnectionStateChanged += (_, state) =>
        {
            if (state) tcs.TrySetResult(true);
        };

        await server.StartAsync(_port);

        var device = new DeviceInfo
        {
            DeviceId = "h", DeviceName = "H",
            IPAddress = "127.0.0.1", Port = _port,
            Type = DeviceType.Desktop
        };
        await client.ConnectToDeviceAsync(device);

        bool fired = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await client.StopAsync();
        await server.StopAsync();

        Assert.True(fired);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        // Tests clean up after themselves; nothing shared to dispose here.
        await Task.CompletedTask;
    }
}
