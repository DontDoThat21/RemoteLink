namespace RemoteLink.Shared.Models;

/// <summary>
/// Configures the optional relay-server fallback transport.
/// </summary>
public sealed class RelayConfiguration
{
    public bool Enabled { get; set; }
    public string ServerHost { get; set; } = string.Empty;
    public int ServerPort { get; set; } = 12400;
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public bool IsConfigured =>
        Enabled &&
        !string.IsNullOrWhiteSpace(ServerHost) &&
        ServerPort is > 0 and <= 65535;

    public void ApplyTo(DeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!IsConfigured)
        {
            device.SupportsRelay = false;
            device.RelayServerHost = null;
            device.RelayServerPort = null;
            return;
        }

        device.SupportsRelay = true;
        device.RelayServerHost = ServerHost;
        device.RelayServerPort = ServerPort;
    }

    public static RelayConfiguration FromEnvironment(
        string hostEnvironmentVariable = "REMOTELINK_RELAY_HOST",
        string portEnvironmentVariable = "REMOTELINK_RELAY_PORT")
    {
        var host = Environment.GetEnvironmentVariable(hostEnvironmentVariable)?.Trim();
        var portText = Environment.GetEnvironmentVariable(portEnvironmentVariable);
        var hasPort = int.TryParse(portText, out var port);

        return new RelayConfiguration
        {
            Enabled = !string.IsNullOrWhiteSpace(host),
            ServerHost = host ?? string.Empty,
            ServerPort = hasPort ? port : 12400
        };
    }
}

/// <summary>
/// Peer identity metadata shared through the relay control plane.
/// </summary>
public sealed class RelayPeerInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
}

/// <summary>
/// Relay server control/data frame.
/// </summary>
public sealed class RelayFrame
{
    public string MessageType { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public string? SourceDeviceId { get; set; }
    public string? TargetDeviceId { get; set; }
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public RelayPeerInfo? Peer { get; set; }
    public string? Payload { get; set; }
}
