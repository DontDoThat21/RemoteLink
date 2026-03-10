using System.Text.Json;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// JSON-file-backed implementation of <see cref="ISavedDevicesService"/>.
/// Data is stored at <c>%AppData%\RemoteLink\saved_devices.json</c>.
/// </summary>
public sealed class SavedDevicesService : ISavedDevicesService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly ILogger<SavedDevicesService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<SavedDevice> _devices = new();

    public SavedDevicesService(ILogger<SavedDevicesService> logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _filePath = Path.Combine(appData, "RemoteLink", "saved_devices.json");
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("Saved devices file not found at {Path}; starting empty", _filePath);
                _devices = new List<SavedDevice>();
                return;
            }

            var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            _devices = JsonSerializer.Deserialize<List<SavedDevice>>(json, JsonOptions) ?? new List<SavedDevice>();
            _logger.LogInformation("Loaded {Count} saved device(s) from {Path}", _devices.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load saved devices from {Path}", _filePath);
            _devices = new List<SavedDevice>();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_devices, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Saved {Count} device(s) to {Path}", _devices.Count, _filePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    public IReadOnlyList<SavedDevice> GetAll()
    {
        return _devices
            .OrderByDescending(d => d.LastConnected ?? DateTime.MinValue)
            .ToList()
            .AsReadOnly();
    }

    public async Task AddOrUpdateAsync(SavedDevice device, CancellationToken cancellationToken = default)
    {
        device.InternetDeviceId = DeviceIdentityManager.NormalizeInternetDeviceId(device.InternetDeviceId);

        var existing = FindExistingDevice(device);
        if (existing != null)
        {
            existing.FriendlyName = device.FriendlyName;
            existing.DeviceName = device.DeviceName;
            existing.DeviceId = string.IsNullOrWhiteSpace(device.DeviceId) ? existing.DeviceId : device.DeviceId;
            existing.InternetDeviceId = device.InternetDeviceId ?? existing.InternetDeviceId;
            existing.IPAddress = device.IPAddress;
            existing.Port = device.Port;
            existing.SupportsRelay = device.SupportsRelay;
            existing.RelayServerHost = device.RelayServerHost;
            existing.RelayServerPort = device.RelayServerPort;
            existing.Type = device.Type;
            if (device.LastConnected.HasValue)
                existing.LastConnected = device.LastConnected;
        }
        else
        {
            _devices.Add(device);
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        _devices.RemoveAll(d => d.Id == id);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public SavedDevice? FindByDeviceId(string deviceId)
    {
        return _devices.FirstOrDefault(d => d.DeviceId == deviceId);
    }

    public SavedDevice? FindMatchingDevice(DeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return _devices.FirstOrDefault(saved => DeviceIdentityManager.MatchesDevice(saved, device));
    }

    public async Task TouchLastConnectedAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var device = _devices.FirstOrDefault(d => d.DeviceId == deviceId);
        if (device != null)
        {
            device.LastConnected = DateTime.UtcNow;
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private SavedDevice? FindExistingDevice(SavedDevice device)
    {
        if (!string.IsNullOrWhiteSpace(device.DeviceId))
        {
            var existing = _devices.FirstOrDefault(d => d.DeviceId == device.DeviceId);
            if (existing != null)
                return existing;
        }

        var internetDeviceId = DeviceIdentityManager.NormalizeInternetDeviceId(device.InternetDeviceId);
        if (internetDeviceId is null)
            return null;

        return _devices.FirstOrDefault(existing =>
            string.Equals(
                DeviceIdentityManager.NormalizeInternetDeviceId(existing.InternetDeviceId),
                internetDeviceId,
                StringComparison.Ordinal));
    }
}
