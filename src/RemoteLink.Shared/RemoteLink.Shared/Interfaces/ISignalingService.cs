using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Registers local devices with an internet signaling directory and resolves remote device IDs.
/// </summary>
public interface ISignalingService
{
    bool IsConfigured { get; }

    Task StartAsync(DeviceInfo localDevice, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task RegisterDeviceAsync(DeviceInfo localDevice, CancellationToken cancellationToken = default);

    Task<DeviceInfo?> ResolveDeviceAsync(string deviceIdentifier, CancellationToken cancellationToken = default);
}
