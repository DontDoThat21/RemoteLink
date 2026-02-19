using RemoteLink.Shared.Models;
using Xunit;

namespace RemoteLink.Shared.Tests.Models;

public class ConnectionQualityTests
{
    [Fact]
    public void GetBandwidthString_ReturnsBytes_WhenLessThan1KB()
    {
        var quality = new ConnectionQuality { Bandwidth = 512 };
        Assert.Equal("512 B/s", quality.GetBandwidthString());
    }

    [Fact]
    public void GetBandwidthString_ReturnsKB_WhenLessThan1MB()
    {
        var quality = new ConnectionQuality { Bandwidth = 50 * 1024 };
        Assert.Equal("50.0 KB/s", quality.GetBandwidthString());
    }

    [Fact]
    public void GetBandwidthString_ReturnsMB_WhenGreaterThan1MB()
    {
        var quality = new ConnectionQuality { Bandwidth = 5 * 1024 * 1024 };
        Assert.Equal("5.0 MB/s", quality.GetBandwidthString());
    }

    [Fact]
    public void GetBandwidthString_ReturnsDecimal_WhenNotExactMB()
    {
        var quality = new ConnectionQuality { Bandwidth = (long)(2.5 * 1024 * 1024) };
        Assert.Equal("2.5 MB/s", quality.GetBandwidthString());
    }

    [Fact]
    public void CalculateRating_ReturnsExcellent_WhenHighFpsLowLatencyGoodBandwidth()
    {
        var rating = ConnectionQuality.CalculateRating(
            fps: 30,
            latency: 30,
            bandwidth: 5 * 1024 * 1024);

        Assert.Equal(QualityRating.Excellent, rating);
    }

    [Fact]
    public void CalculateRating_ReturnsGood_WhenDecentFpsModerateLatencyAdequateBandwidth()
    {
        var rating = ConnectionQuality.CalculateRating(
            fps: 20,
            latency: 75,
            bandwidth: 2 * 1024 * 1024);

        Assert.Equal(QualityRating.Good, rating);
    }

    [Fact]
    public void CalculateRating_ReturnsFair_WhenAcceptableFpsHigherLatency()
    {
        var rating = ConnectionQuality.CalculateRating(
            fps: 12,
            latency: 150,
            bandwidth: 500 * 1024);

        Assert.Equal(QualityRating.Fair, rating);
    }

    [Fact]
    public void CalculateRating_ReturnsPoor_WhenLowFps()
    {
        var rating = ConnectionQuality.CalculateRating(
            fps: 5,
            latency: 50,
            bandwidth: 3 * 1024 * 1024);

        Assert.Equal(QualityRating.Poor, rating);
    }

    [Fact]
    public void CalculateRating_ReturnsPoor_WhenHighLatency()
    {
        var rating = ConnectionQuality.CalculateRating(
            fps: 25,
            latency: 250,
            bandwidth: 3 * 1024 * 1024);

        Assert.Equal(QualityRating.Poor, rating);
    }

    [Fact]
    public void CalculateRating_BoundaryCase_Excellent_AtThreshold()
    {
        // Exactly at excellent threshold
        var rating = ConnectionQuality.CalculateRating(
            fps: 25,
            latency: 49,
            bandwidth: 3 * 1024 * 1024 + 1);

        Assert.Equal(QualityRating.Excellent, rating);
    }

    [Fact]
    public void CalculateRating_BoundaryCase_Good_JustBelowExcellent()
    {
        // Just below excellent threshold (low FPS)
        var rating = ConnectionQuality.CalculateRating(
            fps: 24,
            latency: 49,
            bandwidth: 3 * 1024 * 1024 + 1);

        Assert.Equal(QualityRating.Good, rating);
    }

    [Fact]
    public void CalculateRating_BoundaryCase_Fair_AtThreshold()
    {
        // Exactly at fair threshold
        var rating = ConnectionQuality.CalculateRating(
            fps: 10,
            latency: 199,
            bandwidth: 500 * 1024);

        Assert.Equal(QualityRating.Fair, rating);
    }

    [Fact]
    public void ConnectionQuality_PropertiesCanBeSet()
    {
        var timestamp = DateTime.UtcNow;
        var quality = new ConnectionQuality
        {
            Fps = 15.5,
            Bandwidth = 1024 * 1024,
            Latency = 75,
            Timestamp = timestamp,
            Rating = QualityRating.Good
        };

        Assert.Equal(15.5, quality.Fps);
        Assert.Equal(1024 * 1024, quality.Bandwidth);
        Assert.Equal(75, quality.Latency);
        Assert.Equal(timestamp, quality.Timestamp);
        Assert.Equal(QualityRating.Good, quality.Rating);
    }
}
