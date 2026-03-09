using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;

namespace RemoteLink.Mobile.Services;

/// <summary>
/// Coordinates the active in-session chat state for the mobile app.
/// </summary>
public class MobileChatSession
{
    private readonly RemoteDesktopClient _client;
    private readonly ILoggerFactory _loggerFactory;
    private readonly object _syncRoot = new();
    private IMessagingService? _messagingService;
    private ICommunicationService? _communicationService;
    private string _localDeviceId = BuildLocalDeviceId();
    private string _localDeviceName = BuildLocalDeviceName();

    public event EventHandler<EventArgs>? SessionStateChanged;
    public event EventHandler<EventArgs>? MessagesChanged;
    public event EventHandler<int>? UnreadCountChanged;

    public MobileChatSession(RemoteDesktopClient client, ILoggerFactory loggerFactory)
    {
        _client = client;
        _loggerFactory = loggerFactory;

        _client.ConnectionStateChanged += OnConnectionStateChanged;

        if (_client.IsConnected)
        {
            EnsureMessagingService();
        }
    }

    public bool HasActiveSession => _client.IsConnected && MessagingService != null;

    public string ConnectedHostName => _client.ConnectedHost?.DeviceName ?? "desktop host";

    public int UnreadCount => MessagingService?.UnreadCount ?? 0;

    public IReadOnlyList<ChatMessage> GetMessages()
    {
        return MessagingService?.GetMessages() ?? Array.Empty<ChatMessage>();
    }

    public bool IsLocalMessage(ChatMessage message)
    {
        return message.SenderId == _localDeviceId;
    }

    public async Task<ChatMessage> SendMessageAsync(string text)
    {
        var messaging = EnsureMessagingService() ?? throw new InvalidOperationException("Chat is not available until a connection is active.");
        var message = await messaging.SendMessageAsync(text);
        RaiseMessagesChanged();
        RaiseUnreadCountChanged();
        return message;
    }

    public async Task MarkAllAsReadAsync()
    {
        var messaging = MessagingService;
        if (messaging == null)
        {
            return;
        }

        var unreadMessages = messaging
            .GetMessages()
            .Where(m => !m.IsRead && !IsLocalMessage(m))
            .ToList();

        foreach (var message in unreadMessages)
        {
            await messaging.MarkAsReadAsync(message.MessageId);
        }

        if (unreadMessages.Count > 0)
        {
            RaiseMessagesChanged();
            RaiseUnreadCountChanged();
        }
    }

    private void OnConnectionStateChanged(object? sender, ClientConnectionState state)
    {
        if (state == ClientConnectionState.Connected)
        {
            EnsureMessagingService();
        }
        else if (state == ClientConnectionState.Disconnected)
        {
            ResetSession();
        }

        RaiseSessionStateChanged();
        RaiseUnreadCountChanged();
    }

    private IMessagingService? MessagingService
    {
        get
        {
            lock (_syncRoot)
            {
                return _messagingService;
            }
        }
    }

    private IMessagingService? EnsureMessagingService()
    {
        lock (_syncRoot)
        {
            var communicationService = _client.CurrentCommunicationService;
            if (!_client.IsConnected || communicationService == null)
            {
                return null;
            }

            if (ReferenceEquals(_communicationService, communicationService) && _messagingService != null)
            {
                return _messagingService;
            }

            DetachMessagingService();

            _communicationService = communicationService;
            var messagingLogger = _loggerFactory.CreateLogger<MessagingService>();
            var messagingService = new MessagingService(messagingLogger, communicationService);
            messagingService.Initialize(_localDeviceId, _localDeviceName);
            messagingService.MessageReceived += OnMessageReceived;
            messagingService.MessageRead += OnMessageRead;

            _messagingService = messagingService;
            return _messagingService;
        }
    }

    private void ResetSession()
    {
        lock (_syncRoot)
        {
            if (_messagingService != null)
            {
                _messagingService.ClearMessages();
            }

            DetachMessagingService();
            _communicationService = null;
        }

        RaiseMessagesChanged();
    }

    private void DetachMessagingService()
    {
        if (_messagingService != null)
        {
            _messagingService.MessageReceived -= OnMessageReceived;
            _messagingService.MessageRead -= OnMessageRead;
            _messagingService = null;
        }
    }

    private void OnMessageReceived(object? sender, ChatMessage message)
    {
        RaiseMessagesChanged();
        RaiseUnreadCountChanged();
    }

    private void OnMessageRead(object? sender, string messageId)
    {
        RaiseMessagesChanged();
        RaiseUnreadCountChanged();
    }

    private void RaiseSessionStateChanged()
    {
        SessionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseMessagesChanged()
    {
        MessagesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseUnreadCountChanged()
    {
        UnreadCountChanged?.Invoke(this, UnreadCount);
    }

    private static string BuildLocalDeviceId()
    {
        return $"{Environment.MachineName}_MobileChat";
    }

    private static string BuildLocalDeviceName()
    {
        return $"{Environment.MachineName} Mobile";
    }
}
