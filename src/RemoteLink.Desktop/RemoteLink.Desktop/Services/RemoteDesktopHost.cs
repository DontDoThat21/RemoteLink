using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Main hosted service for the remote desktop host
/// </summary>
public class RemoteDesktopHost : BackgroundService
{
    private readonly ILogger<RemoteDesktopHost> _logger;
    private readonly INetworkDiscovery _networkDiscovery;
    private readonly IScreenCapture _screenCapture;
    private readonly IInputHandler _inputHandler;

    public RemoteDesktopHost(
        ILogger<RemoteDesktopHost> logger,
        INetworkDiscovery networkDiscovery,
        IScreenCapture screenCapture,
        IInputHandler inputHandler)
    {
        _logger = logger;
        _networkDiscovery = networkDiscovery;
        _screenCapture = screenCapture;
        _inputHandler = inputHandler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Remote Desktop Host service starting...");

        try
        {
            // Start network discovery
            await _networkDiscovery.StartBroadcastingAsync();
            await _networkDiscovery.StartListeningAsync();
            
            // Start input handler
            await _inputHandler.StartAsync();
            
            _logger.LogInformation("Remote Desktop Host service started successfully.");
            _logger.LogInformation("Broadcasting presence on local network...");

            // Keep the service running
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Remote Desktop Host service");
            throw;
        }
        finally
        {
            _logger.LogInformation("Remote Desktop Host service stopping...");
            
            // Cleanup
            await _inputHandler.StopAsync();
            await _screenCapture.StopCaptureAsync();
            await _networkDiscovery.StopBroadcastingAsync();
            await _networkDiscovery.StopListeningAsync();
            
            _logger.LogInformation("Remote Desktop Host service stopped.");
        }
    }
}