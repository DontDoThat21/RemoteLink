namespace RemoteLink.Shared.Models;

/// <summary>
/// Commands that a connected client can send to control the active remote session.
/// </summary>
public enum SessionControlCommand
{
    GetMonitors,
    SelectMonitor,
    SetQuality,
    SetImageFormat,
    SetAudioEnabled
}

/// <summary>
/// Request sent from client to host to query or update session settings.
/// </summary>
public class SessionControlRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public SessionControlCommand Command { get; set; }
    public string? MonitorId { get; set; }
    public int? Quality { get; set; }
    public ScreenDataFormat? ImageFormat { get; set; }
    public bool? AudioEnabled { get; set; }
}

/// <summary>
/// Response sent from host to client for a <see cref="SessionControlRequest"/>.
/// </summary>
public class SessionControlResponse
{
    public string RequestId { get; set; } = string.Empty;
    public SessionControlCommand Command { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<MonitorInfo>? Monitors { get; set; }
    public string? SelectedMonitorId { get; set; }
    public int? AppliedQuality { get; set; }
    public ScreenDataFormat? AppliedImageFormat { get; set; }
    public bool? AppliedAudioEnabled { get; set; }
}
