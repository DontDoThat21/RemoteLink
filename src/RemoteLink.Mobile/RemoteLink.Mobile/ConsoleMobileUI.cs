using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RemoteLink.Mobile.Services;
using RemoteLink.Shared.Models;

namespace RemoteLink.Mobile;

/// <summary>
/// Console-based UI service that demonstrates the MAUI functionality
/// This will be replaced by actual MAUI UI when the workload is available
/// </summary>
public class ConsoleMobileUI : BackgroundService
{
    private readonly ILogger<ConsoleMobileUI> _logger;
    private readonly RemoteDesktopClient _remoteDesktopClient;
    private readonly List<RemoteLink.Shared.Models.DeviceInfo> _availableHosts = new();
    private readonly object _hostsLock = new();

    public ConsoleMobileUI(
        ILogger<ConsoleMobileUI> logger,
        RemoteDesktopClient remoteDesktopClient)
    {
        _logger = logger;
        _remoteDesktopClient = remoteDesktopClient;
        
        // Subscribe to events (same as MAUI UI would)
        _remoteDesktopClient.DeviceDiscovered += OnDeviceDiscovered;
        _remoteDesktopClient.DeviceLost += OnDeviceLost;
        _remoteDesktopClient.ServiceStatusChanged += OnServiceStatusChanged;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Console Mobile UI starting...");
        Console.WriteLine("📱 RemoteLink Mobile UI (Console Mode)");
        Console.WriteLine("=====================================");
        Console.WriteLine();

        try
        {
            // Start the remote desktop client (same as MAUI would)
            await _remoteDesktopClient.StartAsync();
            
            // Run the UI loop
            await RunUILoop(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Console Mobile UI");
            throw;
        }
        finally
        {
            _logger.LogInformation("Console Mobile UI stopping...");
            await _remoteDesktopClient.StopAsync();
            _logger.LogInformation("Console Mobile UI stopped.");
        }
    }

    private async Task RunUILoop(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Display the current state (simulating MAUI UI updates)
            DisplayCurrentState();
            
            // Wait before next update
            await Task.Delay(5000, stoppingToken);
        }
    }

    private void DisplayCurrentState()
    {
        Console.Clear();
        
        // Header (simulating MAUI app title)
        Console.WriteLine("📱 RemoteLink Mobile Client");
        Console.WriteLine("===========================");
        Console.WriteLine();
        
        // Status section (simulating MAUI status display)
        Console.WriteLine("🔍 Status: Searching for desktop hosts...");
        Console.WriteLine();
        
        // Available hosts section (simulating MAUI list view)
        lock (_hostsLock)
        {
            if (_availableHosts.Count > 0)
            {
                Console.WriteLine($"🖥️  Available Desktop Hosts ({_availableHosts.Count}):");
                Console.WriteLine("========================================");
                
                for (int i = 0; i < _availableHosts.Count; i++)
                {
                    var host = _availableHosts[i];
                    Console.WriteLine($"{i + 1}. {host.DeviceName}");
                    Console.WriteLine($"   📍 {host.IPAddress}:{host.Port}");
                    Console.WriteLine($"   🆔 {host.DeviceId}");
                    Console.WriteLine($"   [Connect] (Feature available in full MAUI UI)");
                    Console.WriteLine();
                }
            }
            else
            {
                Console.WriteLine("🔍 No desktop hosts found yet...");
                Console.WriteLine("   Make sure a RemoteLink Desktop Host is running on your network.");
                Console.WriteLine();
            }
        }
        
        // Footer (simulating MAUI app info)
        Console.WriteLine("=====================================");
        Console.WriteLine("💡 This is a console preview of the MAUI UI");
        Console.WriteLine("   Install .NET MAUI workload for full GUI experience");
        Console.WriteLine("   Press Ctrl+C to exit");
        Console.WriteLine();
        Console.WriteLine($"⏰ Last updated: {DateTime.Now:HH:mm:ss}");
    }

    private void OnDeviceDiscovered(object? sender, RemoteLink.Shared.Models.DeviceInfo device)
    {
        if (device.Type == RemoteLink.Shared.Models.DeviceType.Desktop)
        {
            lock (_hostsLock)
            {
                if (!_availableHosts.Any(h => h.DeviceId == device.DeviceId))
                {
                    _availableHosts.Add(device);
                    _logger.LogInformation($"✅ Discovered desktop host: {device.DeviceName} at {device.IPAddress}:{device.Port}");
                }
            }
        }
    }

    private void OnDeviceLost(object? sender, RemoteLink.Shared.Models.DeviceInfo device)
    {
        lock (_hostsLock)
        {
            var existingHost = _availableHosts.FirstOrDefault(h => h.DeviceId == device.DeviceId);
            if (existingHost != null)
            {
                _availableHosts.Remove(existingHost);
                _logger.LogInformation($"❌ Lost connection to desktop host: {device.DeviceName}");
            }
        }
    }

    private void OnServiceStatusChanged(object? sender, string status)
    {
        _logger.LogInformation($"📊 Service status: {status}");
    }
}