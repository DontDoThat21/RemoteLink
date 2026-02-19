using RemoteLink.Shared.Interfaces;

namespace RemoteLink.Shared.Services;

/// <summary>
/// Monitors connection performance and recommends adaptive quality settings
/// </summary>
public class PerformanceMonitor : IPerformanceMonitor
{
    private readonly object _lock = new();
    private readonly Queue<FrameMetric> _recentFrames = new();
    private const int MetricWindowSize = 30; // Track last 30 frames

    // Quality adjustment thresholds
    private const long HighLatencyMs = 100;
    private const long MediumLatencyMs = 50;
    private const long HighBandwidthBytesPerSec = 5_000_000; // 5 MB/s
    private const long LowBandwidthBytesPerSec = 1_000_000;  // 1 MB/s

    private class FrameMetric
    {
        public long Timestamp { get; set; }
        public int Bytes { get; set; }
        public long LatencyMs { get; set; }
    }

    /// <summary>
    /// Record that a frame was sent
    /// </summary>
    public void RecordFrameSent(int frameBytes, long latencyMs)
    {
        lock (_lock)
        {
            _recentFrames.Enqueue(new FrameMetric
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Bytes = frameBytes,
                LatencyMs = latencyMs
            });

            // Keep only the most recent frames
            while (_recentFrames.Count > MetricWindowSize)
            {
                _recentFrames.Dequeue();
            }
        }
    }

    /// <summary>
    /// Get the recommended JPEG quality based on current performance (0-100)
    /// </summary>
    public int GetRecommendedQuality()
    {
        lock (_lock)
        {
            if (_recentFrames.Count < 5)
            {
                return 75; // Default quality until we have enough data
            }

            long avgLatency = GetAverageLatency();
            long bandwidth = GetCurrentBandwidth();

            // High latency: reduce quality
            if (avgLatency > HighLatencyMs)
            {
                return 50;
            }

            // Medium latency: moderate quality
            if (avgLatency > MediumLatencyMs)
            {
                return 65;
            }

            // Low bandwidth: reduce quality
            if (bandwidth < LowBandwidthBytesPerSec)
            {
                return 60;
            }

            // Good connection: high quality
            if (bandwidth > HighBandwidthBytesPerSec && avgLatency < MediumLatencyMs)
            {
                return 85;
            }

            // Default: balanced quality
            return 75;
        }
    }

    /// <summary>
    /// Get current frames per second
    /// </summary>
    public double GetCurrentFps()
    {
        lock (_lock)
        {
            if (_recentFrames.Count < 2)
            {
                return 0;
            }

            var frames = _recentFrames.ToArray();
            long timeSpanMs = frames[^1].Timestamp - frames[0].Timestamp;

            if (timeSpanMs == 0)
            {
                return 0;
            }

            return (frames.Length * 1000.0) / timeSpanMs;
        }
    }

    /// <summary>
    /// Get current bandwidth usage in bytes per second
    /// </summary>
    public long GetCurrentBandwidth()
    {
        lock (_lock)
        {
            if (_recentFrames.Count < 2)
            {
                return 0;
            }

            var frames = _recentFrames.ToArray();
            long timeSpanMs = frames[^1].Timestamp - frames[0].Timestamp;

            if (timeSpanMs == 0)
            {
                return 0;
            }

            long totalBytes = frames.Sum(f => f.Bytes);
            return (totalBytes * 1000) / timeSpanMs;
        }
    }

    /// <summary>
    /// Get average latency in milliseconds
    /// </summary>
    public long GetAverageLatency()
    {
        lock (_lock)
        {
            if (_recentFrames.Count == 0)
            {
                return 0;
            }

            return (long)_recentFrames.Average(f => f.LatencyMs);
        }
    }

    /// <summary>
    /// Reset all metrics
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _recentFrames.Clear();
        }
    }
}
