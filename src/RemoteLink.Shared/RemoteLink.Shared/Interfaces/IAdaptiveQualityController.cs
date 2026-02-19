namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Interface for adaptive quality control based on network conditions and performance
/// </summary>
public interface IAdaptiveQualityController
{
    /// <summary>
    /// Current quality level (0-100)
    /// </summary>
    int CurrentQuality { get; }

    /// <summary>
    /// Current target frame rate (FPS)
    /// </summary>
    int CurrentFrameRate { get; }

    /// <summary>
    /// Record that a frame was sent
    /// </summary>
    void RecordFrameSent(int frameSize);

    /// <summary>
    /// Record frame acknowledgment from client (for latency measurement)
    /// </summary>
    void RecordFrameAck(TimeSpan latency);

    /// <summary>
    /// Update quality settings based on performance metrics
    /// </summary>
    void UpdateSettings();

    /// <summary>
    /// Reset to default settings
    /// </summary>
    void Reset();
}
