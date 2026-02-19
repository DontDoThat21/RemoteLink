using RemoteLink.Shared.Interfaces;

namespace RemoteLink.Shared.Services;

/// <summary>
/// Adaptive quality controller that adjusts quality and frame rate based on network conditions
/// </summary>
public class AdaptiveQualityController : IAdaptiveQualityController
{
    private const int DefaultQuality = 75;
    private const int MinQuality = 30;
    private const int MaxQuality = 95;
    
    private const int DefaultFrameRate = 30;
    private const int MinFrameRate = 10;
    private const int MaxFrameRate = 60;
    
    private const int TargetLatencyMs = 100; // Target 100ms latency
    private const int MaxLatencyMs = 500; // Degrade if >500ms
    
    private readonly Queue<int> _recentFrameSizes = new();
    private readonly Queue<TimeSpan> _recentLatencies = new();
    private const int MetricWindowSize = 30; // Track last 30 frames
    
    private int _currentQuality = DefaultQuality;
    private int _currentFrameRate = DefaultFrameRate;
    private DateTime _lastUpdate = DateTime.UtcNow;

    public int CurrentQuality => _currentQuality;
    public int CurrentFrameRate => _currentFrameRate;

    public void RecordFrameSent(int frameSize)
    {
        _recentFrameSizes.Enqueue(frameSize);
        if (_recentFrameSizes.Count > MetricWindowSize)
            _recentFrameSizes.Dequeue();
    }

    public void RecordFrameAck(TimeSpan latency)
    {
        _recentLatencies.Enqueue(latency);
        if (_recentLatencies.Count > MetricWindowSize)
            _recentLatencies.Dequeue();
    }

    public void UpdateSettings()
    {
        // Don't update too frequently
        if ((DateTime.UtcNow - _lastUpdate).TotalSeconds < 2)
            return;
        
        _lastUpdate = DateTime.UtcNow;
        
        // Need enough data to make decisions
        if (_recentLatencies.Count < 5)
            return;
        
        double avgLatencyMs = _recentLatencies.Average(l => l.TotalMilliseconds);
        
        // Adjust quality based on latency
        if (avgLatencyMs > MaxLatencyMs)
        {
            // High latency - reduce quality and frame rate aggressively
            _currentQuality = Math.Max(MinQuality, _currentQuality - 15);
            _currentFrameRate = Math.Max(MinFrameRate, _currentFrameRate - 5);
        }
        else if (avgLatencyMs > TargetLatencyMs * 2)
        {
            // Moderate latency - reduce quality moderately
            _currentQuality = Math.Max(MinQuality, _currentQuality - 10);
            _currentFrameRate = Math.Max(MinFrameRate, _currentFrameRate - 2);
        }
        else if (avgLatencyMs < TargetLatencyMs && _currentQuality < MaxQuality)
        {
            // Good latency - can increase quality
            _currentQuality = Math.Min(MaxQuality, _currentQuality + 5);
            if (_currentFrameRate < DefaultFrameRate)
                _currentFrameRate = Math.Min(MaxFrameRate, _currentFrameRate + 2);
        }
        
        // Adjust frame rate based on frame size (bandwidth consideration)
        if (_recentFrameSizes.Count >= 10)
        {
            double avgFrameSizeKB = _recentFrameSizes.Average() / 1024.0;
            
            // If frames are very large (>500KB), reduce frame rate
            if (avgFrameSizeKB > 500)
            {
                _currentFrameRate = Math.Max(MinFrameRate, _currentFrameRate - 3);
            }
            // If frames are small (<100KB), can increase frame rate
            else if (avgFrameSizeKB < 100 && _currentFrameRate < DefaultFrameRate)
            {
                _currentFrameRate = Math.Min(MaxFrameRate, _currentFrameRate + 2);
            }
        }
    }

    public void Reset()
    {
        _currentQuality = DefaultQuality;
        _currentFrameRate = DefaultFrameRate;
        _recentFrameSizes.Clear();
        _recentLatencies.Clear();
        _lastUpdate = DateTime.UtcNow;
    }
}
