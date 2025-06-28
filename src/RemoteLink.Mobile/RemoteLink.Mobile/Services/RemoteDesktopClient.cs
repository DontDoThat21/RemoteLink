using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Mobile.Services;

/// <summary>
/// Main hosted service for the remote desktop mobile client
/// </summary>
public class RemoteDesktopClient : BackgroundService
{
    private readonly ILogger<RemoteDesktopClient> _logger;
    private readonly INetworkDiscovery _networkDiscovery;
    private readonly List<DeviceInfo> _availableHosts = new();

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Remote Desktop Client service starting...");

        try
        {
            // Start network discovery
            await _networkDiscovery.StartBroadcastingAsync();
            await _networkDiscovery.StartListeningAsync();
            
            _logger.LogInformation("Remote Desktop Client service started successfully.");
            _logger.LogInformation("Listening for desktop hosts on local network...");

            // Simulate client UI interactions
            await SimulateClientInteraction(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Remote Desktop Client service");
            throw;
        }
        finally
        {
            _logger.LogInformation("Remote Desktop Client service stopping...");
            
            // Cleanup
            await _networkDiscovery.StopBroadcastingAsync();
            await _networkDiscovery.StopListeningAsync();
            
            _logger.LogInformation("Remote Desktop Client service stopped.");
        }
    }

    private async Task SimulateClientInteraction(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Display available hosts every 10 seconds
            if (_availableHosts.Count > 0)
            {
                Console.WriteLine($"\n--- Available Desktop Hosts ({_availableHosts.Count}) ---");
                for (int i = 0; i < _availableHosts.Count; i++)
                {
                    var host = _availableHosts[i];
                    Console.WriteLine($"{i + 1}. {host.DeviceName} ({host.IPAddress}:{host.Port})");
                }
                Console.WriteLine("--- End of Host List ---\n");
            }
            else
            {
                Console.WriteLine("Searching for desktop hosts...");
            }

            await Task.Delay(10000, stoppingToken);
        }
    }

    private void OnDeviceDiscovered(object? sender, DeviceInfo device)
    {
        if (device.Type == DeviceType.Desktop)
        {
            lock (_availableHosts)
            {
                if (!_availableHosts.Any(h => h.DeviceId == device.DeviceId))
                {
                    _availableHosts.Add(device);
                    _logger.LogInformation($"Discovered desktop host: {device.DeviceName} at {device.IPAddress}:{device.Port}");
                    Console.WriteLine($"🖥️  Found desktop host: {device.DeviceName} ({device.IPAddress})");
                }
            }
        }
    }

    private void OnDeviceLost(object? sender, DeviceInfo device)
    {
        if (device.Type == DeviceType.Desktop)
        {
            lock (_availableHosts)
            {
                var existingHost = _availableHosts.FirstOrDefault(h => h.DeviceId == device.DeviceId);
                if (existingHost != null)
                {
                    _availableHosts.Remove(existingHost);
                    _logger.LogInformation($"Lost connection to desktop host: {device.DeviceName}");
                    Console.WriteLine($"❌ Lost desktop host: {device.DeviceName}");
                }
            }
        }
    }
}