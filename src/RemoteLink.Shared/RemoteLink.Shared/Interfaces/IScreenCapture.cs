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
    /// Event fired when a new frame is captured
    /// </summary>
    event EventHandler<ScreenData> FrameCaptured;
}