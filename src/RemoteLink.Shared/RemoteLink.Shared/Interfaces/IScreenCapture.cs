using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Interface for capturing screen content on the host device
/// </summary>
public interface IScreenCapture
{
    /// <summary>
    /// Start screen capture
    /// </summary>
    Task StartCaptureAsync();

    /// <summary>
    /// Stop screen capture
    /// </summary>
    Task StopCaptureAsync();

    /// <summary>
    /// Capture a single frame of the screen
    /// </summary>
    Task<ScreenData> CaptureFrameAsync();

    /// <summary>
    /// Get screen dimensions
    /// </summary>
    Task<(int Width, int Height)> GetScreenDimensionsAsync();

    /// <summary>
    /// Set capture quality (0-100)
    /// </summary>
    void SetQuality(int quality);

    /// <summary>
    /// Get all available monitors
    /// </summary>
    Task<IReadOnlyList<MonitorInfo>> GetMonitorsAsync();

    /// <summary>
    /// Select a specific monitor for capture (by monitor ID)
    /// </summary>
    /// <returns>True if the monitor was found and selected, false otherwise</returns>
    Task<bool> SelectMonitorAsync(string monitorId);

    /// <summary>
    /// Get the currently selected monitor ID (null if primary/default)
    /// </summary>
    string? GetSelectedMonitorId();

    /// <summary>
    /// Event fired when a new frame is captured
    /// </summary>
    event EventHandler<ScreenData> FrameCaptured;
}