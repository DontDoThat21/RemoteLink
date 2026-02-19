using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Shared.Tests;

public class AdaptiveQualityControllerTests
{
    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        // Arrange & Act
        var controller = new AdaptiveQualityController();

        // Assert
        Assert.Equal(75, controller.CurrentQuality);
        Assert.Equal(30, controller.CurrentFrameRate);
    }

    [Fact]
    public void RecordFrameSent_AcceptsFrameSize()
    {
        // Arrange
        var controller = new AdaptiveQualityController();

        // Act & Assert (should not throw)
        controller.RecordFrameSent(100000);
        controller.RecordFrameSent(50000);
    }

    [Fact]
    public void RecordFrameAck_AcceptsLatency()
    {
        // Arrange
        var controller = new AdaptiveQualityController();

        // Act & Assert (should not throw)
        controller.RecordFrameAck(TimeSpan.FromMilliseconds(50));
        controller.RecordFrameAck(TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public void UpdateSettings_WithInsufficientData_DoesNotChange()
    {
        // Arrange
        var controller = new AdaptiveQualityController();
        int initialQuality = controller.CurrentQuality;
        int initialFrameRate = controller.CurrentFrameRate;

        // Act - record only 3 latencies (need 5+)
        controller.RecordFrameAck(TimeSpan.FromMilliseconds(100));
        controller.RecordFrameAck(TimeSpan.FromMilliseconds(100));
        controller.RecordFrameAck(TimeSpan.FromMilliseconds(100));
        controller.UpdateSettings();

        // Assert
        Assert.Equal(initialQuality, controller.CurrentQuality);
        Assert.Equal(initialFrameRate, controller.CurrentFrameRate);
    }

    [Fact]
    public void UpdateSettings_WithHighLatency_ReducesQualityAndFrameRate()
    {
        // Arrange
        var controller = new AdaptiveQualityController();
        int initialQuality = controller.CurrentQuality;
        int initialFrameRate = controller.CurrentFrameRate;

        // Act - simulate high latency (>500ms)
        for (int i = 0; i < 10; i++)
        {
            controller.RecordFrameAck(TimeSpan.FromMilliseconds(600));
        }
        
        // Wait for update interval
        Thread.Sleep(2100);
        controller.UpdateSettings();

        // Assert
        Assert.True(controller.CurrentQuality < initialQuality);
        Assert.True(controller.CurrentFrameRate < initialFrameRate);
    }

    [Fact]
    public void UpdateSettings_WithModerateLatency_ReducesQuality()
    {
        // Arrange
        var controller = new AdaptiveQualityController();
        int initialQuality = controller.CurrentQuality;

        // Act - simulate moderate latency (200-500ms)
        for (int i = 0; i < 10; i++)
        {
            controller.RecordFrameAck(TimeSpan.FromMilliseconds(250));
        }
        
        Thread.Sleep(2100);
        controller.UpdateSettings();

        // Assert
        Assert.True(controller.CurrentQuality <= initialQuality);
    }

    [Fact]
    public void UpdateSettings_WithLowLatency_MaintainsOrIncreasesQuality()
    {
        // Arrange
        var controller = new AdaptiveQualityController();
        
        // Start with reduced quality
        for (int i = 0; i < 10; i++)
        {
            controller.RecordFrameAck(TimeSpan.FromMilliseconds(600));
        }
        Thread.Sleep(2100);
        controller.UpdateSettings();
        
        int reducedQuality = controller.CurrentQuality;

        // Act - now simulate good latency
        for (int i = 0; i < 10; i++)
        {
            controller.RecordFrameAck(TimeSpan.FromMilliseconds(50));
        }
        Thread.Sleep(2100);
        controller.UpdateSettings();

        // Assert - quality should increase back
        Assert.True(controller.CurrentQuality >= reducedQuality);
    }

    [Fact]
    public void UpdateSettings_WithLargeFrames_ReducesFrameRate()
    {
        // Arrange
        var controller = new AdaptiveQualityController();
        int initialFrameRate = controller.CurrentFrameRate;

        // Act - simulate large frames (>500KB) with acceptable latency
        for (int i = 0; i < 15; i++)
        {
            controller.RecordFrameSent(600 * 1024); // 600KB frames
            controller.RecordFrameAck(TimeSpan.FromMilliseconds(100));
        }
        
        Thread.Sleep(2100);
        controller.UpdateSettings();

        // Assert
        Assert.True(controller.CurrentFrameRate <= initialFrameRate);
    }

    [Fact]
    public void UpdateSettings_WithSmallFrames_IncreasesFrameRate()
    {
        // Arrange
        var controller = new AdaptiveQualityController();
        
        // First reduce frame rate
        for (int i = 0; i < 15; i++)
        {
            controller.RecordFrameSent(600 * 1024);
            controller.RecordFrameAck(TimeSpan.FromMilliseconds(100));
        }
        Thread.Sleep(2100);
        controller.UpdateSettings();
        
        int reducedFrameRate = controller.CurrentFrameRate;

        // Act - now simulate small frames
        for (int i = 0; i < 15; i++)
        {
            controller.RecordFrameSent(50 * 1024); // 50KB frames
            controller.RecordFrameAck(TimeSpan.FromMilliseconds(50));
        }
        Thread.Sleep(2100);
        controller.UpdateSettings();

        // Assert
        Assert.True(controller.CurrentFrameRate >= reducedFrameRate);
    }

    [Fact]
    public void UpdateSettings_EnforcesMinimumQuality()
    {
        // Arrange
        var controller = new AdaptiveQualityController();

        // Act - simulate terrible latency repeatedly
        for (int iteration = 0; iteration < 5; iteration++)
        {
            for (int i = 0; i < 10; i++)
            {
                controller.RecordFrameAck(TimeSpan.FromMilliseconds(1000));
            }
            Thread.Sleep(2100);
            controller.UpdateSettings();
        }

        // Assert - should not go below minimum
        Assert.True(controller.CurrentQuality >= 30);
    }

    [Fact]
    public void UpdateSettings_EnforcesMaximumQuality()
    {
        // Arrange
        var controller = new AdaptiveQualityController();

        // Act - simulate perfect latency repeatedly
        for (int iteration = 0; iteration < 10; iteration++)
        {
            for (int i = 0; i < 10; i++)
            {
                controller.RecordFrameAck(TimeSpan.FromMilliseconds(10));
            }
            Thread.Sleep(2100);
            controller.UpdateSettings();
        }

        // Assert - should not exceed maximum
        Assert.True(controller.CurrentQuality <= 95);
    }

    [Fact]
    public void UpdateSettings_EnforcesMinimumFrameRate()
    {
        // Arrange
        var controller = new AdaptiveQualityController();

        // Act - simulate terrible conditions
        for (int iteration = 0; iteration < 5; iteration++)
        {
            for (int i = 0; i < 10; i++)
            {
                controller.RecordFrameAck(TimeSpan.FromMilliseconds(1000));
            }
            Thread.Sleep(2100);
            controller.UpdateSettings();
        }

        // Assert
        Assert.True(controller.CurrentFrameRate >= 10);
    }

    [Fact]
    public void UpdateSettings_EnforcesMaximumFrameRate()
    {
        // Arrange
        var controller = new AdaptiveQualityController();

        // Act - simulate perfect conditions
        for (int iteration = 0; iteration < 10; iteration++)
        {
            for (int i = 0; i < 10; i++)
            {
                controller.RecordFrameSent(10 * 1024);
                controller.RecordFrameAck(TimeSpan.FromMilliseconds(10));
            }
            Thread.Sleep(2100);
            controller.UpdateSettings();
        }

        // Assert
        Assert.True(controller.CurrentFrameRate <= 60);
    }

    [Fact]
    public void UpdateSettings_RespectsUpdateInterval()
    {
        // Arrange
        var controller = new AdaptiveQualityController();
        
        for (int i = 0; i < 10; i++)
        {
            controller.RecordFrameAck(TimeSpan.FromMilliseconds(600));
        }

        // Act - call UpdateSettings twice rapidly
        Thread.Sleep(2100);
        controller.UpdateSettings();
        int qualityAfterFirst = controller.CurrentQuality;
        
        controller.UpdateSettings(); // Should be ignored (too soon)
        int qualityAfterSecond = controller.CurrentQuality;

        // Assert - second call should not change settings
        Assert.Equal(qualityAfterFirst, qualityAfterSecond);
    }

    [Fact]
    public void Reset_RestoresDefaultSettings()
    {
        // Arrange
        var controller = new AdaptiveQualityController();
        
        // Degrade settings
        for (int i = 0; i < 10; i++)
        {
            controller.RecordFrameAck(TimeSpan.FromMilliseconds(600));
        }
        Thread.Sleep(2100);
        controller.UpdateSettings();

        // Act
        controller.Reset();

        // Assert
        Assert.Equal(75, controller.CurrentQuality);
        Assert.Equal(30, controller.CurrentFrameRate);
    }

    [Fact]
    public void Reset_ClearsMetrics()
    {
        // Arrange
        var controller = new AdaptiveQualityController();
        
        for (int i = 0; i < 10; i++)
        {
            controller.RecordFrameSent(100000);
            controller.RecordFrameAck(TimeSpan.FromMilliseconds(100));
        }

        // Act
        controller.Reset();
        
        // Should not have enough data to update after reset
        controller.UpdateSettings();

        // Assert - settings should remain at defaults
        Assert.Equal(75, controller.CurrentQuality);
        Assert.Equal(30, controller.CurrentFrameRate);
    }
}
