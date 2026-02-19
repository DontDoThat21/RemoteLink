namespace RemoteLink.Shared.Models;

/// <summary>
/// Represents a text message sent during a remote desktop session.
/// </summary>
public class ChatMessage
{
    /// <summary>
    /// Unique identifier for this message.
    /// </summary>
    public string MessageId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Identifier of the sender (device ID or session ID).
    /// </summary>
    public string SenderId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the sender.
    /// </summary>
    public string SenderName { get; set; } = string.Empty;

    /// <summary>
    /// Message text content.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the message was sent.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether the message has been read/acknowledged by the recipient.
    /// </summary>
    public bool IsRead { get; set; }

    /// <summary>
    /// Optional message type for system notifications (e.g., "user_connected", "file_transfer_started").
    /// Null for regular user messages.
    /// </summary>
    public string? MessageType { get; set; }
}
