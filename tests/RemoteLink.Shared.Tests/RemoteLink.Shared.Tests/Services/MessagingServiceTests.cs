using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Shared.Tests.Services;

public class MessagingServiceTests
{
    // ── Test doubles ──────────────────────────────────────────────────────────

    private class FakeCommunicationService : ICommunicationService
    {
        public List<ChatMessage> SentChatMessages { get; } = new();
        public List<string> SentReadAcks { get; } = new();

        public bool IsConnected => true;

        public event EventHandler<ScreenData>? ScreenDataReceived;
        public event EventHandler<InputEvent>? InputEventReceived;
        public event EventHandler<bool>? ConnectionStateChanged;
        public event EventHandler<PairingRequest>? PairingRequestReceived;
        public event EventHandler<PairingResponse>? PairingResponseReceived;
        public event EventHandler<ConnectionQuality>? ConnectionQualityReceived;
        public event EventHandler<ClipboardData>? ClipboardDataReceived;
        public event EventHandler<FileTransferRequest>? FileTransferRequestReceived;
        public event EventHandler<FileTransferResponse>? FileTransferResponseReceived;
        public event EventHandler<FileTransferChunk>? FileTransferChunkReceived;
        public event EventHandler<FileTransferComplete>? FileTransferCompleteReceived;
        public event EventHandler<AudioData>? AudioDataReceived;
        public event EventHandler<ChatMessage>? ChatMessageReceived;
        public event EventHandler<string>? MessageReadReceived;
        public event EventHandler<PrintJob>? PrintJobReceived;
        public event EventHandler<PrintJobResponse>? PrintJobResponseReceived;
        public event EventHandler<PrintJobStatus>? PrintJobStatusReceived;

        public Task StartAsync(int port) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public Task<bool> ConnectToDeviceAsync(DeviceInfo device) => Task.FromResult(true);
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task SendScreenDataAsync(ScreenData screenData) => Task.CompletedTask;
        public Task SendInputEventAsync(InputEvent inputEvent) => Task.CompletedTask;
        public Task SendPairingRequestAsync(PairingRequest request) => Task.CompletedTask;
        public Task SendPairingResponseAsync(PairingResponse response) => Task.CompletedTask;
        public Task SendConnectionQualityAsync(ConnectionQuality quality) => Task.CompletedTask;
        public Task SendClipboardDataAsync(ClipboardData clipboardData) => Task.CompletedTask;
        public Task SendFileTransferRequestAsync(FileTransferRequest request) => Task.CompletedTask;
        public Task SendFileTransferResponseAsync(FileTransferResponse response) => Task.CompletedTask;
        public Task SendFileTransferChunkAsync(FileTransferChunk chunk) => Task.CompletedTask;
        public Task SendFileTransferCompleteAsync(FileTransferComplete complete) => Task.CompletedTask;
        public Task SendAudioDataAsync(AudioData audioData) => Task.CompletedTask;
        public Task SendPrintJobAsync(PrintJob printJob) => Task.CompletedTask;
        public Task SendPrintJobResponseAsync(PrintJobResponse response) => Task.CompletedTask;
        public Task SendPrintJobStatusAsync(PrintJobStatus status) => Task.CompletedTask;

        public Task SendChatMessageAsync(ChatMessage message)
        {
            SentChatMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task SendMessageReadAsync(string messageId)
        {
            SentReadAcks.Add(messageId);
            return Task.CompletedTask;
        }

        public void RaiseChatMessageReceived(ChatMessage message)
        {
            ChatMessageReceived?.Invoke(this, message);
        }

        public void RaiseMessageReadReceived(string messageId)
        {
            MessageReadReceived?.Invoke(this, messageId);
        }
    }

    // ── Constructor tests ─────────────────────────────────────────────────────

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var comm = new FakeCommunicationService();
        Assert.Throws<ArgumentNullException>(() => new MessagingService(null!, comm));
    }

    [Fact]
    public void Constructor_WithNullCommunication_ThrowsArgumentNullException()
    {
        var logger = NullLogger<MessagingService>.Instance;
        Assert.Throws<ArgumentNullException>(() => new MessagingService(logger, null!));
    }

    [Fact]
    public void Constructor_WithValidArguments_CreatesInstance()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);

        Assert.NotNull(service);
        Assert.Equal(0, service.UnreadCount);
        Assert.Empty(service.GetMessages());
    }

    // ── Initialize tests ──────────────────────────────────────────────────────

    [Fact]
    public void Initialize_WithValidParameters_UpdatesDeviceInfo()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);

        service.Initialize("device-123", "Test Device");

        // Verify by sending a message and checking sender info
        var message = service.SendMessageAsync("Hello").Result;
        Assert.Equal("device-123", message.SenderId);
        Assert.Equal("Test Device", message.SenderName);
    }

    // ── SendMessageAsync tests ────────────────────────────────────────────────

    [Fact]
    public async Task SendMessageAsync_WithNullText_ThrowsArgumentException()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendMessageAsync(null!));
    }

    [Fact]
    public async Task SendMessageAsync_WithEmptyText_ThrowsArgumentException()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendMessageAsync(""));
    }

    [Fact]
    public async Task SendMessageAsync_WithWhitespaceText_ThrowsArgumentException()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendMessageAsync("   "));
    }

    [Fact]
    public async Task SendMessageAsync_WithValidText_CreatesMessageWithId()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);
        service.Initialize("device-1", "Device 1");

        var message = await service.SendMessageAsync("Hello World");

        Assert.NotNull(message);
        Assert.NotNull(message.MessageId);
        Assert.NotEmpty(message.MessageId);
        Assert.Equal("Hello World", message.Text);
        Assert.Equal("device-1", message.SenderId);
        Assert.Equal("Device 1", message.SenderName);
        Assert.True(message.IsRead); // Own messages are marked as read
        Assert.Null(message.MessageType);
    }

    [Fact]
    public async Task SendMessageAsync_WithMessageType_SetsMessageType()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);

        var message = await service.SendMessageAsync("System notification", "system");

        Assert.Equal("system", message.MessageType);
    }

    [Fact]
    public async Task SendMessageAsync_TrimsWhitespace()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);

        var message = await service.SendMessageAsync("  Hello  ");

        Assert.Equal("Hello", message.Text);
    }

    [Fact]
    public async Task SendMessageAsync_AddsToMessageList()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);

        await service.SendMessageAsync("Message 1");
        await service.SendMessageAsync("Message 2");

        var messages = service.GetMessages();
        Assert.Equal(2, messages.Count);
        Assert.Equal("Message 1", messages[0].Text);
        Assert.Equal("Message 2", messages[1].Text);
    }

    [Fact]
    public async Task SendMessageAsync_SendsViaCommunicationService()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);

        await service.SendMessageAsync("Test");

        Assert.Single(comm.SentChatMessages);
        Assert.Equal("Test", comm.SentChatMessages[0].Text);
    }

    // ── MarkAsReadAsync tests ─────────────────────────────────────────────────

    [Fact]
    public async Task MarkAsReadAsync_WithNullMessageId_DoesNothing()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);

        await service.MarkAsReadAsync(null!);

        Assert.Empty(comm.SentReadAcks);
    }

    [Fact]
    public async Task MarkAsReadAsync_WithEmptyMessageId_DoesNothing()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);

        await service.MarkAsReadAsync("");

        Assert.Empty(comm.SentReadAcks);
    }

    [Fact]
    public async Task MarkAsReadAsync_WithUnknownMessageId_DoesNothing()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);

        await service.MarkAsReadAsync("unknown-id");

        Assert.Empty(comm.SentReadAcks);
    }

    [Fact]
    public async Task MarkAsReadAsync_WithValidMessageId_MarksAsRead()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);
        service.Initialize("local", "Local");

        // Receive a message from remote
        var remoteMsg = new ChatMessage
        {
            MessageId = "msg-1",
            SenderId = "remote",
            SenderName = "Remote",
            Text = "Hello",
            IsRead = false
        };
        comm.RaiseChatMessageReceived(remoteMsg);

        await service.MarkAsReadAsync("msg-1");

        var messages = service.GetMessages();
        Assert.Single(messages);
        Assert.True(messages[0].IsRead);
        Assert.Single(comm.SentReadAcks);
        Assert.Equal("msg-1", comm.SentReadAcks[0]);
    }

    [Fact]
    public async Task MarkAsReadAsync_AlreadyRead_DoesNotSendAck()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);
        service.Initialize("local", "Local");

        var remoteMsg = new ChatMessage
        {
            MessageId = "msg-1",
            SenderId = "remote",
            SenderName = "Remote",
            Text = "Hello",
            IsRead = true // Already read
        };
        comm.RaiseChatMessageReceived(remoteMsg);

        await service.MarkAsReadAsync("msg-1");

        Assert.Empty(comm.SentReadAcks); // Should not send ack if already read
    }

    [Fact]
    public async Task MarkAsReadAsync_FiresMessageReadEvent()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);
        service.Initialize("local", "Local");

        var remoteMsg = new ChatMessage
        {
            MessageId = "msg-1",
            SenderId = "remote",
            SenderName = "Remote",
            Text = "Hello",
            IsRead = false
        };
        comm.RaiseChatMessageReceived(remoteMsg);

        string? firedMessageId = null;
        service.MessageRead += (sender, messageId) => firedMessageId = messageId;

        await service.MarkAsReadAsync("msg-1");

        Assert.Equal("msg-1", firedMessageId);
    }

    // ── GetMessages tests ─────────────────────────────────────────────────────

    [Fact]
    public void GetMessages_WhenEmpty_ReturnsEmptyList()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);

        var messages = service.GetMessages();

        Assert.NotNull(messages);
        Assert.Empty(messages);
    }

    [Fact]
    public async Task GetMessages_ReturnsOrderedByTimestamp()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);

        var msg1 = await service.SendMessageAsync("First");
        await Task.Delay(10);
        var msg2 = await service.SendMessageAsync("Second");
        await Task.Delay(10);
        var msg3 = await service.SendMessageAsync("Third");

        var messages = service.GetMessages();

        Assert.Equal(3, messages.Count);
        Assert.Equal("First", messages[0].Text);
        Assert.Equal("Second", messages[1].Text);
        Assert.Equal("Third", messages[2].Text);
    }

    // ── UnreadCount tests ─────────────────────────────────────────────────────

    [Fact]
    public void UnreadCount_WhenEmpty_ReturnsZero()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);

        Assert.Equal(0, service.UnreadCount);
    }

    [Fact]
    public async Task UnreadCount_ExcludesOwnMessages()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);
        service.Initialize("local", "Local");

        await service.SendMessageAsync("My message");

        Assert.Equal(0, service.UnreadCount); // Own messages don't count as unread
    }

    [Fact]
    public void UnreadCount_IncludesUnreadRemoteMessages()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);
        service.Initialize("local", "Local");

        var msg1 = new ChatMessage
        {
            MessageId = "msg-1",
            SenderId = "remote",
            SenderName = "Remote",
            Text = "Hello",
            IsRead = false
        };
        comm.RaiseChatMessageReceived(msg1);

        Assert.Equal(1, service.UnreadCount);
    }

    [Fact]
    public void UnreadCount_ExcludesReadRemoteMessages()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);
        service.Initialize("local", "Local");

        var msg1 = new ChatMessage
        {
            MessageId = "msg-1",
            SenderId = "remote",
            SenderName = "Remote",
            Text = "Hello",
            IsRead = true
        };
        comm.RaiseChatMessageReceived(msg1);

        Assert.Equal(0, service.UnreadCount);
    }

    // ── ClearMessages tests ───────────────────────────────────────────────────

    [Fact]
    public async Task ClearMessages_RemovesAllMessages()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);

        await service.SendMessageAsync("Message 1");
        await service.SendMessageAsync("Message 2");

        service.ClearMessages();

        Assert.Empty(service.GetMessages());
        Assert.Equal(0, service.UnreadCount);
    }

    // ── Event tests ───────────────────────────────────────────────────────────

    [Fact]
    public void MessageReceived_FiresOnRemoteMessage()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);
        service.Initialize("local", "Local");

        ChatMessage? receivedMessage = null;
        service.MessageReceived += (sender, msg) => receivedMessage = msg;

        var remoteMsg = new ChatMessage
        {
            MessageId = "msg-1",
            SenderId = "remote",
            SenderName = "Remote",
            Text = "Hello from remote"
        };
        comm.RaiseChatMessageReceived(remoteMsg);

        Assert.NotNull(receivedMessage);
        Assert.Equal("msg-1", receivedMessage.MessageId);
        Assert.Equal("Hello from remote", receivedMessage.Text);
    }

    [Fact]
    public void MessageReceived_DoesNotFireForDuplicateMessages()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);
        service.Initialize("local", "Local");

        int fireCount = 0;
        service.MessageReceived += (sender, msg) => fireCount++;

        var remoteMsg = new ChatMessage
        {
            MessageId = "msg-1",
            SenderId = "remote",
            SenderName = "Remote",
            Text = "Hello"
        };

        comm.RaiseChatMessageReceived(remoteMsg);
        comm.RaiseChatMessageReceived(remoteMsg); // Duplicate

        Assert.Equal(1, fireCount); // Should only fire once
        Assert.Single(service.GetMessages());
    }

    [Fact]
    public void MessageReadReceived_UpdatesMessageReadStatus()
    {
        var logger = NullLogger<MessagingService>.Instance;
        var comm = new FakeCommunicationService();
        var service = new MessagingService(logger, comm);
        service.Initialize("local", "Local");

        var sentMsg = service.SendMessageAsync("My message").Result;

        comm.RaiseMessageReadReceived(sentMsg.MessageId);

        var messages = service.GetMessages();
        Assert.Single(messages);
        Assert.True(messages[0].IsRead);
    }
}
