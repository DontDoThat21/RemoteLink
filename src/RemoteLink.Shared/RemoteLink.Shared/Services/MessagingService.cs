using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// Default implementation of IMessagingService for in-session chat.
/// </summary>
public class MessagingService : IMessagingService
{
    private readonly ILogger<MessagingService> _logger;
    private readonly ICommunicationService _communicationService;
    private readonly List<ChatMessage> _messages = new();
    private readonly object _lock = new();
    private string _localDeviceId = string.Empty;
    private string _localDeviceName = "Unknown Device";

    public event EventHandler<ChatMessage>? MessageReceived;
    public event EventHandler<string>? MessageRead;

    public int UnreadCount
    {
        get
        {
            lock (_lock)
            {
                return _messages.Count(m => !m.IsRead && m.SenderId != _localDeviceId);
            }
        }
    }

    public MessagingService(ILogger<MessagingService> logger, ICommunicationService communicationService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _communicationService = communicationService ?? throw new ArgumentNullException(nameof(communicationService));

        _communicationService.ChatMessageReceived += OnChatMessageReceived;
        _communicationService.MessageReadReceived += OnMessageReadReceived;
    }

    /// <summary>
    /// Initialize the messaging service with local device information.
    /// </summary>
    public void Initialize(string deviceId, string deviceName)
    {
        _localDeviceId = deviceId ?? string.Empty;
        _localDeviceName = deviceName ?? "Unknown Device";
        _logger.LogDebug("MessagingService initialized for device {DeviceId} ({DeviceName})", deviceId, deviceName);
    }

    public async Task<ChatMessage> SendMessageAsync(string text, string? messageType = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Message text cannot be empty.", nameof(text));
        }

        var message = new ChatMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            SenderId = _localDeviceId,
            SenderName = _localDeviceName,
            Text = text.Trim(),
            Timestamp = DateTime.UtcNow,
            IsRead = true, // Our own messages are considered "read"
            MessageType = messageType
        };

        lock (_lock)
        {
            _messages.Add(message);
        }

        await _communicationService.SendChatMessageAsync(message);
        _logger.LogInformation("Sent chat message: {Text} (Type: {Type})", text, messageType ?? "user");

        return message;
    }

    public async Task MarkAsReadAsync(string messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return;
        }

        bool updated = false;
        lock (_lock)
        {
            var message = _messages.FirstOrDefault(m => m.MessageId == messageId);
            if (message != null && !message.IsRead)
            {
                message.IsRead = true;
                updated = true;
            }
        }

        if (updated)
        {
            await _communicationService.SendMessageReadAsync(messageId);
            MessageRead?.Invoke(this, messageId);
            _logger.LogDebug("Marked message {MessageId} as read", messageId);
        }
    }

    public IReadOnlyList<ChatMessage> GetMessages()
    {
        lock (_lock)
        {
            return _messages.OrderBy(m => m.Timestamp).ToList();
        }
    }

    public void ClearMessages()
    {
        lock (_lock)
        {
            _messages.Clear();
        }
        _logger.LogDebug("Cleared all messages");
    }

    private void OnChatMessageReceived(object? sender, ChatMessage message)
    {
        lock (_lock)
        {
            // Don't add duplicates
            if (_messages.Any(m => m.MessageId == message.MessageId))
            {
                return;
            }

            _messages.Add(message);
        }

        MessageReceived?.Invoke(this, message);
        _logger.LogInformation("Received chat message from {Sender}: {Text}", message.SenderName, message.Text);
    }

    private void OnMessageReadReceived(object? sender, string messageId)
    {
        bool updated = false;
        lock (_lock)
        {
            var message = _messages.FirstOrDefault(m => m.MessageId == messageId);
            if (message != null && !message.IsRead)
            {
                message.IsRead = true;
                updated = true;
            }
        }

        if (updated)
        {
            MessageRead?.Invoke(this, messageId);
            _logger.LogDebug("Remote marked message {MessageId} as read", messageId);
        }
    }
}
