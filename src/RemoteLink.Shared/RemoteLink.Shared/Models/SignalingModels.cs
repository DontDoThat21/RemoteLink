namespace RemoteLink.Shared.Models;

/// <summary>
/// Configures the optional signaling directory used to resolve internet-facing device IDs.
/// </summary>
public sealed class SignalingConfiguration
{
    public bool Enabled { get; set; }
    public string ServerHost { get; set; } = string.Empty;
    public int ServerPort { get; set; } = 12410;
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    public bool IsConfigured =>
        Enabled &&
        !string.IsNullOrWhiteSpace(ServerHost) &&
        ServerPort is > 0 and <= 65535;

    public static SignalingConfiguration FromEnvironment(
        string hostEnvironmentVariable = "REMOTELINK_SIGNALING_HOST",
        string portEnvironmentVariable = "REMOTELINK_SIGNALING_PORT")
    {
        var explicitHost = Environment.GetEnvironmentVariable(hostEnvironmentVariable)?.Trim();
        var explicitPort = Environment.GetEnvironmentVariable(portEnvironmentVariable);

        var fallbackHost = Environment.GetEnvironmentVariable("REMOTELINK_RELAY_HOST")?.Trim();
        var host = string.IsNullOrWhiteSpace(explicitHost) ? fallbackHost : explicitHost;
        var hasPort = int.TryParse(explicitPort, out var port);

        return new SignalingConfiguration
        {
            Enabled = !string.IsNullOrWhiteSpace(host),
            ServerHost = host ?? string.Empty,
            ServerPort = hasPort ? port : 12410
        };
    }
}

/// <summary>
/// Signaling protocol frame used for registration and lookup operations.
/// </summary>
public sealed class SignalingFrame
{
    public string MessageType { get; set; } = string.Empty;
    public string? TargetDeviceId { get; set; }
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public DeviceInfo? Device { get; set; }
}
