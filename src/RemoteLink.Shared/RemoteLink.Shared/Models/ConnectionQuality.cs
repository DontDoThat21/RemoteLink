namespace RemoteLink.Shared.Models;

/// <summary>
/// Represents connection quality metrics
/// </summary>
public class ConnectionQuality
{
    /// <summary>
    /// Current frames per second
    /// </summary>
    public double Fps { get; set; }

    /// <summary>
    /// Current bandwidth usage in bytes per second
    /// </summary>
    public long Bandwidth { get; set; }

    /// <summary>
    /// Average latency in milliseconds
    /// </summary>
    public long Latency { get; set; }

    /// <summary>
    /// Timestamp when metrics were captured
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Overall quality rating (Excellent, Good, Fair, Poor)
    /// </summary>
    public QualityRating Rating { get; set; }

    /// <summary>
    /// Get human-readable bandwidth string (e.g. "2.5 MB/s")
    /// </summary>
    public string GetBandwidthString()
    {
        if (Bandwidth < 1024)
            return $"{Bandwidth} B/s";
        if (Bandwidth < 1024 * 1024)
            return $"{Bandwidth / 1024.0:F1} KB/s";
        return $"{Bandwidth / (1024.0 * 1024.0):F1} MB/s";
    }

    /// <summary>
    /// Calculate quality rating based on metrics
    /// </summary>
    public static QualityRating CalculateRating(double fps, long latency, long bandwidth)
    {
        // Excellent: high FPS, low latency, good bandwidth
        if (fps >= 25 && latency < 50 && bandwidth > 3 * 1024 * 1024)
            return QualityRating.Excellent;

        // Good: decent FPS, moderate latency, adequate bandwidth
        if (fps >= 15 && latency < 100 && bandwidth > 1 * 1024 * 1024)
            return QualityRating.Good;

        // Fair: acceptable FPS, higher latency or lower bandwidth
        if (fps >= 10 && latency < 200)
            return QualityRating.Fair;

        // Poor: low FPS, high latency, or very low bandwidth
        return QualityRating.Poor;
    }
}

/// <summary>
/// Quality rating levels
/// </summary>
public enum QualityRating
{
    /// <summary>
    /// Excellent connection quality (>25 FPS, &lt;50ms latency, >3 MB/s)
    /// </summary>
    Excellent,

    /// <summary>
    /// Good connection quality (>15 FPS, &lt;100ms latency, >1 MB/s)
    /// </summary>
    Good,

    /// <summary>
    /// Fair connection quality (>10 FPS, &lt;200ms latency)
    /// </summary>
    Fair,

    /// <summary>
    /// Poor connection quality
    /// </summary>
    Poor
}
