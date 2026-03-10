using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Provides CRUD operations for the user's saved-device address book.
/// Implementations persist data across sessions.
/// </summary>
public interface ISavedDevicesService
{
    /// <summary>Loads saved devices from persistent storage.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the current list to storage.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all saved devices, ordered by most-recently connected first.</summary>
    IReadOnlyList<SavedDevice> GetAll();

    /// <summary>Adds or updates a device in the address book. Matches on <see cref="SavedDevice.DeviceId"/>.</summary>
    Task AddOrUpdateAsync(SavedDevice device, CancellationToken cancellationToken = default);

    /// <summary>Removes a device by its saved-entry ID.</summary>
    Task RemoveAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Finds a saved device by its network DeviceId.</summary>
    SavedDevice? FindByDeviceId(string deviceId);

    /// <summary>Finds a saved device by matching local or internet-facing device IDs.</summary>
    SavedDevice? FindMatchingDevice(DeviceInfo device);

    /// <summary>Updates the <see cref="SavedDevice.LastConnected"/> timestamp for the given DeviceId.</summary>
    Task TouchLastConnectedAsync(string deviceId, CancellationToken cancellationToken = default);
}
