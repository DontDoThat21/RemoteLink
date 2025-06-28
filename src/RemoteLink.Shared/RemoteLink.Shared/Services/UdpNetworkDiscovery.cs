using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace RemoteLink.Shared.Services;

/// <summary>
/// UDP-based network discovery service
/// </summary>
public class UdpNetworkDiscovery : INetworkDiscovery
{
    private const int DISCOVERY_PORT = 12345;
    private const int BROADCAST_INTERVAL_MS = 5000;
    private const int DEVICE_TIMEOUT_MS = 15000;

    private UdpClient? _broadcastClient;
    private UdpClient? _listenClient;
    private Timer? _broadcastTimer;
    private Timer? _cleanupTimer;
    private readonly DeviceInfo _localDevice;
    private readonly Dictionary<string, DeviceInfo> _discoveredDevices = new();
    private readonly object _lockObject = new();
    private bool _isRunning;

    public event EventHandler<DeviceInfo>? DeviceDiscovered;
    public event EventHandler<DeviceInfo>? DeviceLost;

    public UdpNetworkDiscovery(DeviceInfo localDevice)
    {
        _localDevice = localDevice;
    }

    public async Task StartBroadcastingAsync()
    {
        if (_isRunning) return;

        _broadcastClient = new UdpClient();
        _broadcastClient.EnableBroadcast = true;
        
        _broadcastTimer = new Timer(BroadcastPresence, null, 0, BROADCAST_INTERVAL_MS);
        _isRunning = true;
        
        await Task.CompletedTask;
    }

    public async Task StopBroadcastingAsync()
    {
        _broadcastTimer?.Dispose();
        _broadcastTimer = null;
        
        _broadcastClient?.Close();
        _broadcastClient?.Dispose();
        _broadcastClient = null;
        
        await Task.CompletedTask;
    }

    public async Task StartListeningAsync()
    {
        if (_listenClient != null) return;

        _listenClient = new UdpClient(DISCOVERY_PORT);
        
        // Start cleanup timer
        _cleanupTimer = new Timer(CleanupOfflineDevices, null, DEVICE_TIMEOUT_MS, DEVICE_TIMEOUT_MS);
        
        // Start listening for broadcasts
        _ = Task.Run(ListenForBroadcasts);
        
        await Task.CompletedTask;
    }

    public async Task StopListeningAsync()
    {
        _cleanupTimer?.Dispose();
        _cleanupTimer = null;
        
        _listenClient?.Close();
        _listenClient?.Dispose();
        _listenClient = null;
        
        await Task.CompletedTask;
    }

    public async Task<IEnumerable<DeviceInfo>> GetDiscoveredDevicesAsync()
    {
        lock (_lockObject)
        {
            return _discoveredDevices.Values.ToList();
        }
    }

    private void BroadcastPresence(object? state)
    {
        try
        {
            if (_broadcastClient == null) return;

            var message = JsonSerializer.Serialize(_localDevice);
            var data = Encoding.UTF8.GetBytes(message);
            var endPoint = new IPEndPoint(IPAddress.Broadcast, DISCOVERY_PORT);
            
            _broadcastClient.Send(data, data.Length, endPoint);
        }
        catch (Exception)
        {
            // Log error in production
        }
    }

    private async Task ListenForBroadcasts()
    {
        while (_listenClient != null)
        {
            try
            {
                var result = await _listenClient.ReceiveAsync();
                var message = Encoding.UTF8.GetString(result.Buffer);
                var device = JsonSerializer.Deserialize<DeviceInfo>(message);
                
                if (device != null && device.DeviceId != _localDevice.DeviceId)
                {
                    ProcessDiscoveredDevice(device, result.RemoteEndPoint.Address.ToString());
                }
            }
            catch (ObjectDisposedException)
            {
                // Expected when shutting down
                break;
            }
            catch (Exception)
            {
                // Log error in production
            }
        }
    }

    private void ProcessDiscoveredDevice(DeviceInfo device, string ipAddress)
    {
        device.IPAddress = ipAddress;
        device.LastSeen = DateTime.UtcNow;
        device.IsOnline = true;

        lock (_lockObject)
        {
            var isNewDevice = !_discoveredDevices.ContainsKey(device.DeviceId);
            _discoveredDevices[device.DeviceId] = device;
            
            if (isNewDevice)
            {
                DeviceDiscovered?.Invoke(this, device);
            }
        }
    }

    private void CleanupOfflineDevices(object? state)
    {
        var timeout = DateTime.UtcNow.AddMilliseconds(-DEVICE_TIMEOUT_MS);
        var lostDevices = new List<DeviceInfo>();

        lock (_lockObject)
        {
            var offlineDevices = _discoveredDevices.Values
                .Where(d => d.LastSeen < timeout)
                .ToList();

            foreach (var device in offlineDevices)
            {
                device.IsOnline = false;
                lostDevices.Add(device);
                _discoveredDevices.Remove(device.DeviceId);
            }
        }

        foreach (var device in lostDevices)
        {
            DeviceLost?.Invoke(this, device);
        }
    }

    public void Dispose()
    {
        _isRunning = false;
        StopBroadcastingAsync().Wait();
        StopListeningAsync().Wait();
    }
}