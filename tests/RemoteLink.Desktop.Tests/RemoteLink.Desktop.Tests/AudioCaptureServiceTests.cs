using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Desktop.Services;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using Xunit;

namespace RemoteLink.Desktop.Tests.Services;

public class AudioCaptureServiceTests
{
    private readonly ILogger<MockAudioCaptureService> _logger;

    public AudioCaptureServiceTests()
    {
        _logger = NullLogger<MockAudioCaptureService>.Instance;
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MockAudioCaptureService(null!));
    }

    [Fact]
    public void IsCapturing_WhenNotStarted_ReturnsFalse()
    {
        // Arrange
        var service = new MockAudioCaptureService(_logger);

        // Act & Assert
        Assert.False(service.IsCapturing);
    }

    [Fact]
    public async Task StartAsync_WhenNotStarted_StartsCapture()
    {
        // Arrange
        var service = new MockAudioCaptureService(_logger);

        // Act
        await service.StartAsync();
        await Task.Delay(100); // Let it start

        // Assert
        Assert.True(service.IsCapturing);

        // Cleanup
        await service.StopAsync();
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyStarted_DoesNotThrow()
    {
        // Arrange
        var service = new MockAudioCaptureService(_logger);
        await service.StartAsync();

        // Act & Assert
        await service.StartAsync(); // Should not throw
        Assert.True(service.IsCapturing);

        // Cleanup
        await service.StopAsync();
    }

    [Fact]
    public async Task StopAsync_WhenNotStarted_DoesNotThrow()
    {
        // Arrange
        var service = new MockAudioCaptureService(_logger);

        // Act & Assert
        await service.StopAsync(); // Should not throw
        Assert.False(service.IsCapturing);
    }

    [Fact]
    public async Task StopAsync_WhenStarted_StopsCapture()
    {
        // Arrange
        var service = new MockAudioCaptureService(_logger);
        await service.StartAsync();
        Assert.True(service.IsCapturing);

        // Act
        await service.StopAsync();

        // Assert
        Assert.False(service.IsCapturing);
    }

    [Fact]
    public async Task AudioCaptured_WhenStarted_FiresEvent()
    {
        // Arrange
        var service = new MockAudioCaptureService(_logger);
        AudioData? capturedData = null;
        service.AudioCaptured += (sender, data) => capturedData = data;

        // Act
        await service.StartAsync();
        await Task.Delay(150); // Wait for at least one chunk (20ms default + processing time)
        await service.StopAsync();

        // Assert
        Assert.NotNull(capturedData);
        Assert.NotEmpty(capturedData.Data);
    }

    [Fact]
    public async Task AudioCaptured_AfterStop_DoesNotFire()
    {
        // Arrange
        var service = new MockAudioCaptureService(_logger);
        int eventCount = 0;
        service.AudioCaptured += (sender, data) => eventCount++;

        // Act
        await service.StartAsync();
        await Task.Delay(150); // Let some events fire
        await service.StopAsync();
        
        int countAfterStop = eventCount;
        await Task.Delay(100); // Wait a bit more

        // Assert
        Assert.Equal(countAfterStop, eventCount); // No new events after stop
    }

    [Fact]
    public void Settings_HasDefaultValues()
    {
        // Arrange
        var service = new MockAudioCaptureService(_logger);

        // Act
        var settings = service.Settings;

        // Assert
        Assert.NotNull(settings);
        Assert.Equal(48000, settings.SampleRate);
        Assert.Equal(2, settings.Channels);
        Assert.Equal(16, settings.BitsPerSample);
        Assert.Equal(20, settings.ChunkDurationMs);
        Assert.True(settings.CaptureLoopback);
    }

    [Fact]
    public void UpdateSettings_WithValidSettings_UpdatesSettings()
    {
        // Arrange
        var service = new MockAudioCaptureService(_logger);
        var newSettings = new AudioCaptureSettings
        {
            SampleRate = 44100,
            Channels = 1,
            BitsPerSample = 32,
            ChunkDurationMs = 10,
            CaptureLoopback = false
        };

        // Act
        service.UpdateSettings(newSettings);

        // Assert
        Assert.Equal(44100, service.Settings.SampleRate);
        Assert.Equal(1, service.Settings.Channels);
        Assert.Equal(32, service.Settings.BitsPerSample);
        Assert.Equal(10, service.Settings.ChunkDurationMs);
        Assert.False(service.Settings.CaptureLoopback);
    }

    [Fact]
    public void UpdateSettings_WithNull_ThrowsArgumentNullException()
    {
        // Arrange
        var service = new MockAudioCaptureService(_logger);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => service.UpdateSettings(null!));
    }

    [Fact]
    public async Task AudioData_HasCorrectFormat()
    {
        // Arrange
        var service = new MockAudioCaptureService(_logger);
        AudioData? capturedData = null;
        service.AudioCaptured += (sender, data) => capturedData = data;

        // Act
        await service.StartAsync();
        await Task.Delay(150);
        await service.StopAsync();

        // Assert
        Assert.NotNull(capturedData);
        Assert.Equal(48000, capturedData.SampleRate);
        Assert.Equal(2, capturedData.Channels);
        Assert.Equal(16, capturedData.BitsPerSample);
        Assert.Equal(20, capturedData.DurationMs);
        Assert.Equal("PCM", capturedData.Format);
        Assert.InRange(capturedData.Timestamp, DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow);
    }

    [Fact]
    public async Task AudioData_BufferSizeMatchesSettings()
    {
        // Arrange
        var service = new MockAudioCaptureService(_logger);
        var settings = new AudioCaptureSettings
        {
            SampleRate = 44100,
            Channels = 1,
            BitsPerSample = 16,
            ChunkDurationMs = 10
        };
        service.UpdateSettings(settings);

        AudioData? capturedData = null;
        service.AudioCaptured += (sender, data) => capturedData = data;

        // Act
        await service.StartAsync();
        await Task.Delay(100);
        await service.StopAsync();

        // Assert
        Assert.NotNull(capturedData);
        
        // Calculate expected buffer size:
        // samplesPerChunk = (sampleRate * durationMs) / 1000 = (44100 * 10) / 1000 = 441
        // bytesPerSample = bitsPerSample / 8 = 16 / 8 = 2
        // bufferSize = samplesPerChunk * channels * bytesPerSample = 441 * 1 * 2 = 882
        int expectedSize = (settings.SampleRate * settings.ChunkDurationMs / 1000) 
                          * settings.Channels 
                          * (settings.BitsPerSample / 8);
        Assert.Equal(expectedSize, capturedData.Data.Length);
    }

    [Fact]
    public async Task StartAsync_FollowedByStop_SetsIsCapturingCorrectly()
    {
        // Arrange
        var service = new MockAudioCaptureService(_logger);
        
        // Act
        await service.StartAsync();
        Assert.True(service.IsCapturing);
        
        await service.StopAsync();
        
        // Assert
        Assert.False(service.IsCapturing);
    }

    [Fact]
    public async Task AudioCaptured_MultipleChunks_AllHaveCorrectDuration()
    {
        // Arrange
        var service = new MockAudioCaptureService(_logger);
        var capturedChunks = new List<AudioData>();
        service.AudioCaptured += (sender, data) => capturedChunks.Add(data);

        // Act
        await service.StartAsync();
        await Task.Delay(250); // Enough time for multiple chunks (20ms each)
        await service.StopAsync();

        // Assert
        Assert.True(capturedChunks.Count >= 5, $"Expected at least 5 chunks, got {capturedChunks.Count}");
        Assert.All(capturedChunks, chunk => Assert.Equal(20, chunk.DurationMs));
    }
}
