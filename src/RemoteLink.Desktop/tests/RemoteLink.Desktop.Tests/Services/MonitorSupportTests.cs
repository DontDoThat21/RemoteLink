using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Desktop.Services;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Tests.Services;

/// <summary>
/// Tests for multi-monitor support in WindowsScreenCapture.
/// Note: These tests run on Linux CI where EnumDisplayMonitors will return mock data.
/// Real multi-monitor behavior requires Windows with multiple displays.
/// </summary>
public class MonitorSupportTests
{
    private readonly WindowsScreenCapture _screenCapture;

    public MonitorSupportTests()
    {
        _screenCapture = new WindowsScreenCapture(NullLogger<WindowsScreenCapture>.Instance);
    }

    [Fact]
    public async Task GetMonitorsAsync_ReturnsAtLeastOneMonitor()
    {
        // Act
        var monitors = await _screenCapture.GetMonitorsAsync();

        // Assert
        Assert.NotNull(monitors);
        Assert.NotEmpty(monitors);
    }

    [Fact]
    public async Task GetMonitorsAsync_PrimaryMonitorExists()
    {
        // Act
        var monitors = await _screenCapture.GetMonitorsAsync();

        // Assert
        Assert.Contains(monitors, m => m.IsPrimary);
    }

    [Fact]
    public async Task GetMonitorsAsync_MonitorHasValidDimensions()
    {
        // Act
        var monitors = await _screenCapture.GetMonitorsAsync();
        var firstMonitor = monitors.First();

        // Assert
        Assert.True(firstMonitor.Width > 0, "Monitor width should be positive");
        Assert.True(firstMonitor.Height > 0, "Monitor height should be positive");
    }

    [Fact]
    public async Task GetMonitorsAsync_MonitorHasId()
    {
        // Act
        var monitors = await _screenCapture.GetMonitorsAsync();
        var firstMonitor = monitors.First();

        // Assert
        Assert.NotNull(firstMonitor.Id);
        Assert.NotEmpty(firstMonitor.Id);
    }

    [Fact]
    public async Task GetMonitorsAsync_MonitorHasName()
    {
        // Act
        var monitors = await _screenCapture.GetMonitorsAsync();
        var firstMonitor = monitors.First();

        // Assert
        Assert.NotNull(firstMonitor.Name);
        Assert.NotEmpty(firstMonitor.Name);
    }

    [Fact]
    public async Task SelectMonitorAsync_ReturnsTrue()
    {
        // Arrange
        var monitors = await _screenCapture.GetMonitorsAsync();
        var firstMonitor = monitors.First();

        // Act
        var result = await _screenCapture.SelectMonitorAsync(firstMonitor.Id);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetSelectedMonitorId_ReturnsNullByDefault()
    {
        // Act
        var selectedId = _screenCapture.GetSelectedMonitorId();

        // Assert
        Assert.Null(selectedId);
    }

    [Fact]
    public async Task SelectMonitorAsync_UpdatesSelectedMonitorId()
    {
        // Arrange
        var monitors = await _screenCapture.GetMonitorsAsync();
        var firstMonitor = monitors.First();

        // Act
        await _screenCapture.SelectMonitorAsync(firstMonitor.Id);
        var selectedId = _screenCapture.GetSelectedMonitorId();

        // Assert
        Assert.Equal(firstMonitor.Id, selectedId);
    }

    [Fact]
    public async Task GetScreenDimensionsAsync_ReturnsSelectedMonitorDimensions()
    {
        // Arrange
        var monitors = await _screenCapture.GetMonitorsAsync();
        var firstMonitor = monitors.First();
        await _screenCapture.SelectMonitorAsync(firstMonitor.Id);

        // Act
        var (width, height) = await _screenCapture.GetScreenDimensionsAsync();

        // Assert
        Assert.Equal(firstMonitor.Width, width);
        Assert.Equal(firstMonitor.Height, height);
    }

    [Fact]
    public async Task GetScreenDimensionsAsync_ReturnsPrimaryDimensionsWhenNoneSelected()
    {
        // Act
        var (width, height) = await _screenCapture.GetScreenDimensionsAsync();

        // Assert
        Assert.True(width > 0, "Width should be positive");
        Assert.True(height > 0, "Height should be positive");
    }

    [Fact]
    public async Task CaptureFrameAsync_CapturesSelectedMonitor()
    {
        // Arrange
        var monitors = await _screenCapture.GetMonitorsAsync();
        var firstMonitor = monitors.First();
        await _screenCapture.SelectMonitorAsync(firstMonitor.Id);

        // Act
        var frame = await _screenCapture.CaptureFrameAsync();

        // Assert
        Assert.NotNull(frame);
        Assert.Equal(firstMonitor.Width, frame.Width);
        Assert.Equal(firstMonitor.Height, frame.Height);
    }

    [Fact]
    public async Task SelectMonitorAsync_CanSwitchBetweenMonitors()
    {
        // Arrange
        var monitors = await _screenCapture.GetMonitorsAsync();
        if (monitors.Count < 2)
        {
            // Can't test switching on single monitor system - skip
            return;
        }

        var firstMonitor = monitors[0];
        var secondMonitor = monitors[1];

        // Act - select first monitor
        await _screenCapture.SelectMonitorAsync(firstMonitor.Id);
        var firstSelected = _screenCapture.GetSelectedMonitorId();
        var (width1, height1) = await _screenCapture.GetScreenDimensionsAsync();

        // Act - select second monitor
        await _screenCapture.SelectMonitorAsync(secondMonitor.Id);
        var secondSelected = _screenCapture.GetSelectedMonitorId();
        var (width2, height2) = await _screenCapture.GetScreenDimensionsAsync();

        // Assert
        Assert.Equal(firstMonitor.Id, firstSelected);
        Assert.Equal(secondMonitor.Id, secondSelected);
        Assert.Equal(firstMonitor.Width, width1);
        Assert.Equal(firstMonitor.Height, height1);
        Assert.Equal(secondMonitor.Width, width2);
        Assert.Equal(secondMonitor.Height, height2);
    }

    [Fact]
    public async Task MonitorInfo_RightAndBottomPropertiesCalculatedCorrectly()
    {
        // Arrange
        var monitors = await _screenCapture.GetMonitorsAsync();
        var monitor = monitors.First();

        // Assert
        Assert.Equal(monitor.Left + monitor.Width, monitor.Right);
        Assert.Equal(monitor.Top + monitor.Height, monitor.Bottom);
    }

    [Fact]
    public async Task GetMonitorsAsync_CalledMultipleTimes_ReturnsConsistentResults()
    {
        // Act
        var monitors1 = await _screenCapture.GetMonitorsAsync();
        var monitors2 = await _screenCapture.GetMonitorsAsync();

        // Assert
        Assert.Equal(monitors1.Count, monitors2.Count);
        
        for (int i = 0; i < monitors1.Count; i++)
        {
            Assert.Equal(monitors1[i].Id, monitors2[i].Id);
            Assert.Equal(monitors1[i].Width, monitors2[i].Width);
            Assert.Equal(monitors1[i].Height, monitors2[i].Height);
        }
    }

    [Fact]
    public async Task SelectMonitorAsync_WithInvalidId_StillReturnsTrue()
    {
        // Note: Current implementation always returns true
        // This could be changed to validate monitor ID in the future

        // Act
        var result = await _screenCapture.SelectMonitorAsync("nonexistent-monitor-id");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Dispose_DoesNotThrow_WhenMonitorSelected()
    {
        // Arrange
        using var capture = new WindowsScreenCapture(NullLogger<WindowsScreenCapture>.Instance);
        var monitors = capture.GetMonitorsAsync().Result;
        capture.SelectMonitorAsync(monitors.First().Id).Wait();

        // Act & Assert (no exception)
        capture.Dispose();
    }
}
