namespace RemoteLink.Shared.Tests.Services;

using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

public class FileTransferServiceTests
{
    private readonly FakeCommunicationService _fakeCommunicationService;
    private readonly FileTransferService _service;
    private readonly string _testFilesPath;

    public FileTransferServiceTests()
    {
        var logger = new FakeLogger<FileTransferService>();
        _fakeCommunicationService = new FakeCommunicationService();
        _service = new FileTransferService(logger, _fakeCommunicationService);

        // Create test files directory
        _testFilesPath = Path.Combine(Path.GetTempPath(), "RemoteLink_FileTransferTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testFilesPath);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ThrowsOnNullLogger()
    {
        Assert.Throws<ArgumentNullException>(() => new FileTransferService(null!, _fakeCommunicationService));
    }

    [Fact]
    public void Constructor_ThrowsOnNullCommunicationService()
    {
        var logger = new FakeLogger<FileTransferService>();
        Assert.Throws<ArgumentNullException>(() => new FileTransferService(logger, null!));
    }

    // ── InitiateTransferAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task InitiateTransferAsync_ThrowsOnNullFilePath()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.InitiateTransferAsync(null!, FileTransferDirection.Upload));
    }

    [Fact]
    public async Task InitiateTransferAsync_ThrowsOnEmptyFilePath()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.InitiateTransferAsync("", FileTransferDirection.Upload));
    }

    [Fact]
    public async Task InitiateTransferAsync_ThrowsOnNonExistentFile()
    {
        var nonExistentPath = Path.Combine(_testFilesPath, "does_not_exist.txt");
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _service.InitiateTransferAsync(nonExistentPath, FileTransferDirection.Upload));
    }

    [Fact]
    public async Task InitiateTransferAsync_SendsRequestWithCorrectData()
    {
        // Arrange
        var testFilePath = CreateTestFile("test.txt", "Hello World");

        // Act
        var transferId = await _service.InitiateTransferAsync(testFilePath, FileTransferDirection.Upload);

        // Assert
        Assert.NotNull(transferId);
        Assert.NotEmpty(transferId);
        Assert.Single(_fakeCommunicationService.SentFileTransferRequests);

        var request = _fakeCommunicationService.SentFileTransferRequests[0];
        Assert.Equal(transferId, request.TransferId);
        Assert.Equal("test.txt", request.FileName);
        Assert.Equal(11, request.FileSize); // "Hello World" = 11 bytes
        Assert.Equal(FileTransferDirection.Upload, request.Direction);
        Assert.Equal("text/plain", request.MimeType);
    }

    [Fact]
    public async Task InitiateTransferAsync_AddsToActiveTransfers()
    {
        // Arrange
        var testFilePath = CreateTestFile("active.txt", "data");

        // Act
        var transferId = await _service.InitiateTransferAsync(testFilePath, FileTransferDirection.Upload);

        // Assert
        var activeTransfers = _service.GetActiveTransfers();
        Assert.Contains(transferId, activeTransfers);
    }

    [Fact]
    public async Task InitiateTransferAsync_DetectsMimeTypeForPdf()
    {
        // Arrange
        var testFilePath = CreateTestFile("document.pdf", "fake PDF content");

        // Act
        await _service.InitiateTransferAsync(testFilePath, FileTransferDirection.Download);

        // Assert
        var request = _fakeCommunicationService.SentFileTransferRequests[0];
        Assert.Equal("application/pdf", request.MimeType);
    }

    [Fact]
    public async Task InitiateTransferAsync_DetectsMimeTypeForImage()
    {
        // Arrange
        var testFilePath = CreateTestFile("photo.jpg", "fake JPEG data");

        // Act
        await _service.InitiateTransferAsync(testFilePath, FileTransferDirection.Upload);

        // Assert
        var request = _fakeCommunicationService.SentFileTransferRequests[0];
        Assert.Equal("image/jpeg", request.MimeType);
    }

    // ── AcceptTransferAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task AcceptTransferAsync_ThrowsOnNullTransferId()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.AcceptTransferAsync(null!, "/tmp/file.txt"));
    }

    [Fact]
    public async Task AcceptTransferAsync_ThrowsOnNullSavePath()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.AcceptTransferAsync("transfer-123", null!));
    }

    [Fact]
    public async Task AcceptTransferAsync_SendsAcceptResponse()
    {
        // Arrange
        var transferId = "test-transfer-123";
        var savePath = Path.Combine(_testFilesPath, "received.txt");

        // Act
        var result = await _service.AcceptTransferAsync(transferId, savePath);

        // Assert
        Assert.True(result);
        Assert.Single(_fakeCommunicationService.SentFileTransferResponses);

        var response = _fakeCommunicationService.SentFileTransferResponses[0];
        Assert.Equal(transferId, response.TransferId);
        Assert.True(response.Accepted);
    }

    [Fact]
    public async Task AcceptTransferAsync_AddsToActiveTransfers()
    {
        // Arrange
        var transferId = "test-transfer-456";
        var savePath = Path.Combine(_testFilesPath, "download.dat");

        // Act
        await _service.AcceptTransferAsync(transferId, savePath);

        // Assert
        var activeTransfers = _service.GetActiveTransfers();
        Assert.Contains(transferId, activeTransfers);
    }

    // ── RejectTransferAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task RejectTransferAsync_ThrowsOnNullTransferId()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.RejectTransferAsync(null!, FileTransferRejectionReason.FileTooLarge));
    }

    [Fact]
    public async Task RejectTransferAsync_SendsRejectResponse()
    {
        // Arrange
        var transferId = "reject-123";

        // Act
        var result = await _service.RejectTransferAsync(transferId, FileTransferRejectionReason.UserDeclined);

        // Assert
        Assert.True(result);
        Assert.Single(_fakeCommunicationService.SentFileTransferResponses);

        var response = _fakeCommunicationService.SentFileTransferResponses[0];
        Assert.Equal(transferId, response.TransferId);
        Assert.False(response.Accepted);
        Assert.Equal(FileTransferRejectionReason.UserDeclined, response.RejectionReason);
    }

    // ── CancelTransferAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task CancelTransferAsync_ReturnsFalseForUnknownTransferId()
    {
        // Act
        var result = await _service.CancelTransferAsync("unknown-transfer-789");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CancelTransferAsync_RemovesFromActiveTransfers()
    {
        // Arrange
        var testFilePath = CreateTestFile("cancel.txt", "test data");
        var transferId = await _service.InitiateTransferAsync(testFilePath, FileTransferDirection.Upload);

        // Act
        var result = await _service.CancelTransferAsync(transferId);

        // Assert
        Assert.True(result);
        var activeTransfers = _service.GetActiveTransfers();
        Assert.DoesNotContain(transferId, activeTransfers);
    }

    [Fact]
    public async Task CancelTransferAsync_SendsCancelNotification()
    {
        // Arrange
        var testFilePath = CreateTestFile("cancel2.txt", "data");
        var transferId = await _service.InitiateTransferAsync(testFilePath, FileTransferDirection.Upload);

        // Act
        await _service.CancelTransferAsync(transferId);

        // Assert
        Assert.Single(_fakeCommunicationService.SentFileTransferCompletes);

        var complete = _fakeCommunicationService.SentFileTransferCompletes[0];
        Assert.Equal(transferId, complete.TransferId);
        Assert.False(complete.Success);
        Assert.Contains("cancelled", complete.ErrorMessage?.ToLowerInvariant());
    }

    // ── GetProgress ───────────────────────────────────────────────────────────

    [Fact]
    public void GetProgress_ReturnsNullForUnknownTransferId()
    {
        // Act
        var progress = _service.GetProgress("unknown-999");

        // Assert
        Assert.Null(progress);
    }

    [Fact]
    public async Task GetProgress_ReturnsProgressForActiveTransfer()
    {
        // Arrange
        var testFilePath = CreateTestFile("progress.txt", "test content");
        var transferId = await _service.InitiateTransferAsync(testFilePath, FileTransferDirection.Upload);

        // Act
        var progress = _service.GetProgress(transferId);

        // Assert
        Assert.NotNull(progress);
        Assert.Equal(transferId, progress.TransferId);
        Assert.Equal(0, progress.BytesTransferred);
        Assert.Equal(12, progress.TotalBytes); // "test content" = 12 bytes
    }

    // ── GetActiveTransfers ────────────────────────────────────────────────────

    [Fact]
    public void GetActiveTransfers_ReturnsEmptyListInitially()
    {
        // Act
        var activeTransfers = _service.GetActiveTransfers();

        // Assert
        Assert.NotNull(activeTransfers);
        Assert.Empty(activeTransfers);
    }

    [Fact]
    public async Task GetActiveTransfers_ReturnsMultipleTransfers()
    {
        // Arrange
        var file1 = CreateTestFile("file1.txt", "data1");
        var file2 = CreateTestFile("file2.txt", "data2");

        // Act
        var transfer1 = await _service.InitiateTransferAsync(file1, FileTransferDirection.Upload);
        var transfer2 = await _service.InitiateTransferAsync(file2, FileTransferDirection.Download);

        // Assert
        var activeTransfers = _service.GetActiveTransfers();
        Assert.Equal(2, activeTransfers.Count);
        Assert.Contains(transfer1, activeTransfers);
        Assert.Contains(transfer2, activeTransfers);
    }

    // ── Events ────────────────────────────────────────────────────────────────

    [Fact]
    public void TransferRequested_FiresWhenRequestReceived()
    {
        // Arrange
        FileTransferRequest? receivedRequest = null;
        _service.TransferRequested += (s, r) => receivedRequest = r;

        var request = new FileTransferRequest
        {
            TransferId = "event-test-1",
            FileName = "test.txt",
            FileSize = 1024,
            Direction = FileTransferDirection.Upload
        };

        // Act
        _fakeCommunicationService.SimulateFileTransferRequestReceived(request);

        // Assert
        Assert.NotNull(receivedRequest);
        Assert.Equal("event-test-1", receivedRequest.TransferId);
        Assert.Equal("test.txt", receivedRequest.FileName);
    }

    [Fact]
    public void TransferResponseReceived_FiresWhenResponseReceived()
    {
        // Arrange
        FileTransferResponse? receivedResponse = null;
        _service.TransferResponseReceived += (s, r) => receivedResponse = r;

        var response = new FileTransferResponse
        {
            TransferId = "event-test-2",
            Accepted = true
        };

        // Act
        _fakeCommunicationService.SimulateFileTransferResponseReceived(response);

        // Assert
        Assert.NotNull(receivedResponse);
        Assert.Equal("event-test-2", receivedResponse.TransferId);
        Assert.True(receivedResponse.Accepted);
    }

    [Fact]
    public async Task ChunkReceived_CreatesFile()
    {
        // Arrange
        var savePath = Path.Combine(_testFilesPath, "chunk_test.txt");
        var transferId = "chunk-test-123";
        await _service.AcceptTransferAsync(transferId, savePath);

        var chunk = new FileTransferChunk
        {
            TransferId = transferId,
            Offset = 0,
            Data = System.Text.Encoding.UTF8.GetBytes("Hello"),
            Length = 5,
            IsLastChunk = true
        };

        // Act
        _fakeCommunicationService.SimulateFileTransferChunkReceived(chunk);
        await Task.Delay(100); // Allow async processing

        // Assert
        Assert.True(File.Exists(savePath));
        var content = await File.ReadAllTextAsync(savePath);
        Assert.Equal("Hello", content);
    }

    [Fact]
    public async Task ChunkReceived_FiresProgressUpdated()
    {
        // Arrange
        var savePath = Path.Combine(_testFilesPath, "progress_test.txt");
        var transferId = "progress-test-456";
        await _service.AcceptTransferAsync(transferId, savePath);

        FileTransferProgress? progressUpdate = null;
        _service.ProgressUpdated += (s, p) => progressUpdate = p;

        var chunk = new FileTransferChunk
        {
            TransferId = transferId,
            Offset = 0,
            Data = new byte[100],
            Length = 100,
            IsLastChunk = false
        };

        // Act
        _fakeCommunicationService.SimulateFileTransferChunkReceived(chunk);
        await Task.Delay(100); // Allow async processing

        // Assert
        Assert.NotNull(progressUpdate);
        Assert.Equal(transferId, progressUpdate.TransferId);
        Assert.Equal(100, progressUpdate.BytesTransferred);
    }

    [Fact]
    public async Task ChunkReceived_FiresTransferCompleteOnLastChunk()
    {
        // Arrange
        var savePath = Path.Combine(_testFilesPath, "complete_test.txt");
        var transferId = "complete-test-789";
        await _service.AcceptTransferAsync(transferId, savePath);

        FileTransferComplete? completeEvent = null;
        _service.TransferCompleted += (s, c) => completeEvent = c;

        var chunk = new FileTransferChunk
        {
            TransferId = transferId,
            Offset = 0,
            Data = System.Text.Encoding.UTF8.GetBytes("Done"),
            Length = 4,
            IsLastChunk = true
        };

        // Act
        _fakeCommunicationService.SimulateFileTransferChunkReceived(chunk);
        await Task.Delay(100); // Allow async processing

        // Assert
        Assert.NotNull(completeEvent);
        Assert.Equal(transferId, completeEvent.TransferId);
        Assert.True(completeEvent.Success);
        Assert.Equal(savePath, completeEvent.SavedPath);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string CreateTestFile(string fileName, string content)
    {
        var filePath = Path.Combine(_testFilesPath, fileName);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    // ── Fakes ─────────────────────────────────────────────────────────────────

    private class FakeLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private class FakeCommunicationService : ICommunicationService
    {
        public List<FileTransferRequest> SentFileTransferRequests { get; } = new();
        public List<FileTransferResponse> SentFileTransferResponses { get; } = new();
        public List<FileTransferChunk> SentFileTransferChunks { get; } = new();
        public List<FileTransferComplete> SentFileTransferCompletes { get; } = new();

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
        public Task SendAudioDataAsync(AudioData audioData) => Task.CompletedTask;

        public Task SendFileTransferRequestAsync(FileTransferRequest request)
        {
            SentFileTransferRequests.Add(request);
            return Task.CompletedTask;
        }

        public Task SendFileTransferResponseAsync(FileTransferResponse response)
        {
            SentFileTransferResponses.Add(response);
            return Task.CompletedTask;
        }

        public Task SendFileTransferChunkAsync(FileTransferChunk chunk)
        {
            SentFileTransferChunks.Add(chunk);
            return Task.CompletedTask;
        }

        public Task SendFileTransferCompleteAsync(FileTransferComplete complete)
        {
            SentFileTransferCompletes.Add(complete);
            return Task.CompletedTask;
        }

        public Task SendChatMessageAsync(ChatMessage message) => Task.CompletedTask;
        public Task SendMessageReadAsync(string messageId) => Task.CompletedTask;
        public Task SendPrintJobAsync(PrintJob printJob) => Task.CompletedTask;
        public Task SendPrintJobResponseAsync(PrintJobResponse response) => Task.CompletedTask;
        public Task SendPrintJobStatusAsync(PrintJobStatus status) => Task.CompletedTask;

        public void SimulateFileTransferRequestReceived(FileTransferRequest request)
        {
            FileTransferRequestReceived?.Invoke(this, request);
        }

        public void SimulateFileTransferResponseReceived(FileTransferResponse response)
        {
            FileTransferResponseReceived?.Invoke(this, response);
        }

        public void SimulateFileTransferChunkReceived(FileTransferChunk chunk)
        {
            FileTransferChunkReceived?.Invoke(this, chunk);
        }

        public void SimulateFileTransferCompleteReceived(FileTransferComplete complete)
        {
            FileTransferCompleteReceived?.Invoke(this, complete);
        }
    }
}
