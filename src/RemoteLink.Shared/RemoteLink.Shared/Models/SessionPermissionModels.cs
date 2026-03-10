namespace RemoteLink.Shared.Models;

/// <summary>
/// Effective permissions granted for a single remote-control session.
/// </summary>
public sealed class SessionPermissionSet
{
    /// <summary>
    /// Allows the remote peer to send keyboard/mouse/input events.
    /// When false, the session is effectively view-only.
    /// </summary>
    public bool AllowRemoteInput { get; set; } = true;

    /// <summary>
    /// Allows bidirectional clipboard synchronization.
    /// </summary>
    public bool AllowClipboardSync { get; set; } = true;

    /// <summary>
    /// Allows file-transfer features for the session.
    /// </summary>
    public bool AllowFileTransfer { get; set; } = true;

    /// <summary>
    /// Allows host audio streaming for the session.
    /// </summary>
    public bool AllowAudioStreaming { get; set; } = true;

    /// <summary>
    /// Allows remote session-control operations such as monitor switching,
    /// quality changes, and image-format changes.
    /// </summary>
    public bool AllowSessionControl { get; set; } = true;

    public static SessionPermissionSet CreateFullAccess() => new();

    public SessionPermissionSet Clone()
    {
        return new SessionPermissionSet
        {
            AllowRemoteInput = AllowRemoteInput,
            AllowClipboardSync = AllowClipboardSync,
            AllowFileTransfer = AllowFileTransfer,
            AllowAudioStreaming = AllowAudioStreaming,
            AllowSessionControl = AllowSessionControl
        };
    }
}
