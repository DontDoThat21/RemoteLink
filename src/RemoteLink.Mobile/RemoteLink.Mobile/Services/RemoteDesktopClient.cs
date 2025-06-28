using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Mobile.Services;

/// <summary>
/// Main service for the remote desktop mobile client
/// </summary>
public class RemoteDesktopClient
{
    private readonly ILogger<RemoteDesktopClient> _logger;
    private readonly INetworkDiscovery _networkDiscovery;
    private bool _isStarted;

    public event EventHandler<RemoteLink.Shared.Models.DeviceInfo>? DeviceDiscovered;
    public event EventHandler<RemoteLink.Shared.Models.DeviceInfo>? DeviceLost;
    public event EventHandler<string>? ServiceStatusChanged;

    public RemoteDesktopClient(
        ILogger<RemoteDesktopClient> logger,
        INetworkDiscovery networkDiscovery)
    {
        _logger = logger;
        _networkDiscovery = networkDiscovery;
        
        // Subscribe to discovery events
        _networkDiscovery.DeviceDiscovered += OnDeviceDiscovered;
        _networkDiscovery.DeviceLost += OnDeviceLost;
    }

    public async Task StartAsync()
    {
        if (_isStarted) return;

        _logger.LogInformation("Remote Desktop Client service starting...");
        ServiceStatusChanged?.Invoke(this, "Starting discovery service...");

        try
        {
            // Start network discovery
            await _networkDiscovery.StartBroadcastingAsync();
            await _networkDiscovery.StartListeningAsync();
            
            _isStarted = true;
            _logger.LogInformation("Remote Desktop Client service started successfully.");
            ServiceStatusChanged?.Invoke(this, "Listening for desktop hosts...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting Remote Desktop Client service");
            ServiceStatusChanged?.Invoke(this, $"Error: {ex.Message}");
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (!_isStarted) return;

        _logger.LogInformation("Remote Desktop Client service stopping...");
        ServiceStatusChanged?.Invoke(this, "Stopping discovery service...");
        
        try
        {
            // Cleanup
            await _networkDiscovery.StopBroadcastingAsync();
            await _networkDiscovery.StopListeningAsync();
            
            _isStarted = false;
            _logger.LogInformation("Remote Desktop Client service stopped.");
            ServiceStatusChanged?.Invoke(this, "Discovery service stopped.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping Remote Desktop Client service");
            ServiceStatusChanged?.Invoke(this, $"Error stopping: {ex.Message}");
        }
    }

    private void OnDeviceDiscovered(object? sender, RemoteLink.Shared.Models.DeviceInfo device)
    {
        if (device.Type == RemoteLink.Shared.Models.DeviceType.Desktop)
        {
            _logger.LogInformation($"Discovered desktop host: {device.DeviceName} at {device.IPAddress}:{device.Port}");
            DeviceDiscovered?.Invoke(this, device);
        }
    }

    private void OnDeviceLost(object? sender, RemoteLink.Shared.Models.DeviceInfo device)
    {
        if (device.Type == RemoteLink.Shared.Models.DeviceType.Desktop)
        {
            _logger.LogInformation($"Lost connection to desktop host: {device.DeviceName}");
            DeviceLost?.Invoke(this, device);
        }
    }
}