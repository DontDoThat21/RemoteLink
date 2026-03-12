namespace RemoteLink.Shared.Models;

/// <summary>
/// Commands that a connected client can send to control the active remote session.
/// </summary>
public enum SessionControlCommand
{
    GetMonitors,
    GetSystemInformation,
    ExecuteCommand,
    SelectMonitor,
    SetQuality,
    SetImageFormat,
    SetAudioEnabled,
    RebootDevice
}

/// <summary>
/// Request sent from client to host to query or update session settings.
/// </summary>
public class SessionControlRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public SessionControlCommand Command { get; set; }
    public RemoteCommandExecutionRequest? CommandRequest { get; set; }
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
    public RemoteCommandExecutionResult? CommandResult { get; set; }
    public List<MonitorInfo>? Monitors { get; set; }
    public string? SelectedMonitorId { get; set; }
    public RemoteSystemInfo? SystemInfo { get; set; }
    public int? AppliedQuality { get; set; }
    public ScreenDataFormat? AppliedImageFormat { get; set; }
    public bool? AppliedAudioEnabled { get; set; }
    public bool? AutoReconnectSupported { get; set; }
    public int? ReconnectDelaySeconds { get; set; }
}

/// <summary>
/// Shell used when executing a remote command.
/// </summary>
public enum RemoteCommandShell
{
    PowerShell,
    CommandPrompt
}

/// <summary>
/// Request payload for remote command/script execution.
/// </summary>
public class RemoteCommandExecutionRequest
{
    public string CommandText { get; set; } = string.Empty;
    public RemoteCommandShell Shell { get; set; } = RemoteCommandShell.PowerShell;
    public string? WorkingDirectory { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Result payload returned after executing a remote command/script.
/// </summary>
public class RemoteCommandExecutionResult
{
    public RemoteCommandShell Shell { get; set; }
    public string? WorkingDirectory { get; set; }
    public bool Succeeded { get; set; }
    public bool TimedOut { get; set; }
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public long DurationMs { get; set; }
}
