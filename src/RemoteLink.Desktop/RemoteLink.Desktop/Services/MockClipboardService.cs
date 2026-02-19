using RemoteLink.Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Mock clipboard service for non-Windows platforms (testing/development).
/// </summary>
public class MockClipboardService : IClipboardService
{
    private readonly ILogger<MockClipboardService> _logger;
    private bool _isMonitoring;
    private string? _text;

    public event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;

    public bool IsMonitoring => _isMonitoring;

    public MockClipboardService(ILogger<MockClipboardService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isMonitoring)
        {
            _logger.LogDebug("Mock clipboard monitoring already started");
            return Task.CompletedTask;
        }

        _isMonitoring = true;
        _logger.LogInformation("Mock clipboard monitoring started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isMonitoring)
        {
            _logger.LogDebug("Mock clipboard monitoring already stopped");
            return Task.CompletedTask;
        }

        _isMonitoring = false;
        _text = null;
        _logger.LogInformation("Mock clipboard monitoring stopped");
        return Task.CompletedTask;
    }

    public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_text);
    }

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        _text = text;
        _logger.LogInformation("Mock clipboard text set: {Length} chars", text?.Length ?? 0);
        return Task.CompletedTask;
    }

    public Task<byte[]?> GetImageAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<byte[]?>(null);
    }

    public Task SetImageAsync(byte[] pngData, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Mock clipboard image set: {Size} bytes", pngData?.Length ?? 0);
        return Task.CompletedTask;
    }
}
