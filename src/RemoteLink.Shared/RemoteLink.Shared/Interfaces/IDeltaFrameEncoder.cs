using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Interface for encoding delta frames (only changed regions)
/// </summary>
public interface IDeltaFrameEncoder
{
    /// <summary>
    /// Encode a frame as a delta (only changed regions) or full frame.
    /// Returns a tuple of (encoded frame, isDelta).
    /// </summary>
    Task<(ScreenData EncodedFrame, bool IsDelta)> EncodeFrameAsync(ScreenData currentFrame);

    /// <summary>
    /// Reset the encoder state (clears reference frame)
    /// </summary>
    void Reset();

    /// <summary>
    /// Set the minimum changed pixel percentage to trigger delta encoding (0-100)
    /// </summary>
    void SetDeltaThreshold(int percentageThreshold);
}
