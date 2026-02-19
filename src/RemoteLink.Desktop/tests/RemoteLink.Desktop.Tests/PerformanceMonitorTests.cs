using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Desktop.Tests;

public class PerformanceMonitorTests
{
    [Fact]
    public void GetRecommendedQuality_WithNoData_ShouldReturnDefault()
    {
        // Arrange
        var monitor = new PerformanceMonitor();

        // Act
        int quality = monitor.GetRecommendedQuality();

        // Assert
        Assert.Equal(75, quality); // Default quality
    }

    [Fact]
    public void GetRecommendedQuality_WithHighLatency_ShouldReduceQuality()
    {
        // Arrange
        var monitor = new PerformanceMonitor();

        // Act - Record frames with high latency (> 100ms)
        for (int i = 0; i < 10; i++)
        {
            monitor.RecordFrameSent(100_000, 150); // 150ms latency
        }

        int quality = monitor.GetRecommendedQuality();

        // Assert
        Assert.Equal(50, quality); // High latency → low quality
    }

    [Fact]
    public void GetRecommendedQuality_WithMediumLatency_ShouldReturnModerateQuality()
    {
        // Arrange
        var monitor = new PerformanceMonitor();

        // Act - Record frames with medium latency (50-100ms)
        for (int i = 0; i < 10; i++)
        {
            monitor.RecordFrameSent(100_000, 75); // 75ms latency
        }

        int quality = monitor.GetRecommendedQuality();

        // Assert
        Assert.Equal(65, quality); // Medium latency → moderate quality
    }

    [Fact]
    public void GetRecommendedQuality_WithLowBandwidth_ShouldReduceQuality()
    {
        // Arrange
        var monitor = new PerformanceMonitor();

        // Act - Record frames with low bandwidth (< 1 MB/s)
        // Send 50KB frames every 100ms = 500 KB/s
        for (int i = 0; i < 10; i++)
        {
            monitor.RecordFrameSent(50_000, 20); // Low latency but small frames
            Thread.Sleep(100);
        }

        int quality = monitor.GetRecommendedQuality();

        // Assert
        Assert.Equal(60, quality); // Low bandwidth → reduced quality
    }

    [Fact]
    public void GetRecommendedQuality_WithGoodConnection_ShouldReturnHighQuality()
    {
        // Arrange
        var monitor = new PerformanceMonitor();

        // Act - Record frames with high bandwidth and low latency
        // Send 1MB frames every 100ms = 10 MB/s with 20ms latency
        for (int i = 0; i < 10; i++)
        {
            monitor.RecordFrameSent(1_000_000, 20); // High bandwidth, low latency
            Thread.Sleep(100);
        }

        int quality = monitor.GetRecommendedQuality();

        // Assert
        Assert.Equal(85, quality); // Good connection → high quality
    }

    [Fact]
    public void GetCurrentFps_WithNoData_ShouldReturnZero()
    {
        // Arrange
        var monitor = new PerformanceMonitor();

        // Act
        double fps = monitor.GetCurrentFps();

        // Assert
        Assert.Equal(0, fps);
    }

    [Fact]
    public void GetCurrentFps_WithSingleFrame_ShouldReturnZero()
    {
        // Arrange
        var monitor = new PerformanceMonitor();

        // Act
        monitor.RecordFrameSent(100_000, 20);
        double fps = monitor.GetCurrentFps();

        // Assert
        Assert.Equal(0, fps); // Need at least 2 frames to calculate FPS
    }

    [Fact]
    public void GetCurrentFps_ShouldCalculateCorrectly()
    {
        // Arrange
        var monitor = new PerformanceMonitor();

        // Act - Record 10 frames over 1 second = 10 FPS
        for (int i = 0; i < 10; i++)
        {
            monitor.RecordFrameSent(100_000, 20);
            Thread.Sleep(100); // 100ms between frames
        }

        double fps = monitor.GetCurrentFps();

        // Assert
        Assert.InRange(fps, 8.0, 12.0); // Allow some timing variance
    }

    [Fact]
    public void GetCurrentBandwidth_WithNoData_ShouldReturnZero()
    {
        // Arrange
        var monitor = new PerformanceMonitor();

        // Act
        long bandwidth = monitor.GetCurrentBandwidth();

        // Assert
        Assert.Equal(0, bandwidth);
    }

    [Fact]
    public void GetCurrentBandwidth_ShouldCalculateCorrectly()
    {
        // Arrange
        var monitor = new PerformanceMonitor();

        // Act - Record 10 frames of 100KB over 1 second = 1 MB/s
        for (int i = 0; i < 10; i++)
        {
            monitor.RecordFrameSent(100_000, 20);
            Thread.Sleep(100);
        }

        long bandwidth = monitor.GetCurrentBandwidth();

        // Assert
        Assert.InRange(bandwidth, 800_000, 1_200_000); // ~1 MB/s with variance
    }

    [Fact]
    public void GetAverageLatency_WithNoData_ShouldReturnZero()
    {
        // Arrange
        var monitor = new PerformanceMonitor();

        // Act
        long latency = monitor.GetAverageLatency();

        // Assert
        Assert.Equal(0, latency);
    }

    [Fact]
    public void GetAverageLatency_ShouldCalculateCorrectly()
    {
        // Arrange
        var monitor = new PerformanceMonitor();

        // Act
        monitor.RecordFrameSent(100_000, 10);
        monitor.RecordFrameSent(100_000, 20);
        monitor.RecordFrameSent(100_000, 30);

        long avgLatency = monitor.GetAverageLatency();

        // Assert
        Assert.Equal(20, avgLatency); // (10 + 20 + 30) / 3 = 20
    }

    [Fact]
    public void Reset_ShouldClearAllMetrics()
    {
        // Arrange
        var monitor = new PerformanceMonitor();

        // Act - Record data then reset
        monitor.RecordFrameSent(100_000, 50);
        monitor.RecordFrameSent(100_000, 50);
        monitor.Reset();

        // Assert
        Assert.Equal(0, monitor.GetCurrentFps());
        Assert.Equal(0, monitor.GetCurrentBandwidth());
        Assert.Equal(0, monitor.GetAverageLatency());
        Assert.Equal(75, monitor.GetRecommendedQuality()); // Back to default
    }

    [Fact]
    public void MetricWindow_ShouldLimitToLast30Frames()
    {
        // Arrange
        var monitor = new PerformanceMonitor();

        // Act - Record 50 frames (should keep only last 30)
        for (int i = 0; i < 50; i++)
        {
            monitor.RecordFrameSent(100_000, i); // Latency increases
        }

        long avgLatency = monitor.GetAverageLatency();

        // Assert
        // Average of frames 20-49 (last 30)
        // (20 + 21 + ... + 49) / 30 = 34.5
        Assert.InRange(avgLatency, 32, 36);
    }

    [Fact]
    public void ConcurrentAccess_ShouldNotThrow()
    {
        // Arrange
        var monitor = new PerformanceMonitor();
        var tasks = new List<Task>();

        // Act - Multiple threads recording and reading simultaneously
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    monitor.RecordFrameSent(100_000, 20);
                    _ = monitor.GetRecommendedQuality();
                    _ = monitor.GetCurrentFps();
                    _ = monitor.GetCurrentBandwidth();
                    _ = monitor.GetAverageLatency();
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert - No exception thrown
        Assert.True(true);
    }
}
