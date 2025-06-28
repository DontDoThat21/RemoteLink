using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Interface for communication between devices
/// </summary>
public interface ICommunicationService
{
    /// <summary>
    /// Start the communication service
    /// </summary>
    Task StartAsync(int port);

    /// <summary>
    /// Stop the communication service
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Connect to a remote device
    /// </summary>
    Task<bool> ConnectToDeviceAsync(DeviceInfo device);

    /// <summary>
    /// Disconnect from current device
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Send screen data to connected client
    /// </summary>
    Task SendScreenDataAsync(ScreenData screenData);

    /// <summary>
    /// Send input event to connected host
    /// </summary>
    Task SendInputEventAsync(InputEvent inputEvent);

    /// <summary>
    /// Check if currently connected to a device
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Event fired when screen data is received
    /// </summary>
    event EventHandler<ScreenData> ScreenDataReceived;

    /// <summary>
    /// Event fired when input event is received
    /// </summary>
    event EventHandler<InputEvent> InputEventReceived;

    /// <summary>
    /// Event fired when connection state changes
    /// </summary>
    event EventHandler<bool> ConnectionStateChanged;
}