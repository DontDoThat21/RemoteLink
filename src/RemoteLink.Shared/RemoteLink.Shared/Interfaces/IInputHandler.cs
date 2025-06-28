using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Interface for handling input events on the host device
/// </summary>
public interface IInputHandler
{
    /// <summary>
    /// Process an input event received from a remote client
    /// </summary>
    Task ProcessInputEventAsync(InputEvent inputEvent);

    /// <summary>
    /// Start input handling service
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Stop input handling service
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Check if input handling is currently active
    /// </summary>
    bool IsActive { get; }
}