using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Service for sending and receiving chat messages during remote desktop sessions.
/// </summary>
public interface IMessagingService
{
    /// <summary>
    /// Initialize the messaging service with local device information.
    /// Must be called before sending messages.
    /// </summary>
    /// <param name="deviceId">Unique identifier for this device.</param>
    /// <param name="deviceName">Display name for this device.</param>
    void Initialize(string deviceId, string deviceName);

    /// <summary>
    /// Send a text message to the remote party.
    /// </summary>
    /// <param name="text">Message text to send.</param>
    /// <param name="messageType">Optional message type (null for user messages).</param>
    /// <returns>The sent ChatMessage with MessageId populated.</returns>
    Task<ChatMessage> SendMessageAsync(string text, string? messageType = null);

    /// <summary>
    /// Mark a message as read.
    /// </summary>
    /// <param name="messageId">ID of the message to mark as read.</param>
    Task MarkAsReadAsync(string messageId);

    /// <summary>
    /// Get all messages in the current session.
    /// </summary>
    /// <returns>List of messages ordered by timestamp.</returns>
    IReadOnlyList<ChatMessage> GetMessages();

    /// <summary>
    /// Get count of unread messages.
    /// </summary>
    int UnreadCount { get; }

    /// <summary>
    /// Clear all messages (typically when disconnecting).
    /// </summary>
    void ClearMessages();

    /// <summary>
    /// Fired when a new message is received from the remote party.
    /// </summary>
    event EventHandler<ChatMessage>? MessageReceived;

    /// <summary>
    /// Fired when a message has been marked as read.
    /// </summary>
    event EventHandler<string>? MessageRead;
}
