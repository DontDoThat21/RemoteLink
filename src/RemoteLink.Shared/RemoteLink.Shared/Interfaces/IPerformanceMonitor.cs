namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Interface for monitoring connection performance and recommending quality settings
/// </summary>
public interface IPerformanceMonitor
{
    /// <summary>
    /// Record that a frame was sent
    /// </summary>
    void RecordFrameSent(int frameBytes, long latencyMs);

    /// <summary>
    /// Get the recommended JPEG quality based on current performance (0-100)
    /// </summary>
    int GetRecommendedQuality();

    /// <summary>
    /// Get current frames per second
    /// </summary>
    double GetCurrentFps();

    /// <summary>
    /// Get current bandwidth usage in bytes per second
    /// </summary>
    long GetCurrentBandwidth();

    /// <summary>
    /// Get average latency in milliseconds
    /// </summary>
    long GetAverageLatency();

    /// <summary>
    /// Reset all metrics
    /// </summary>
    void Reset();
}
