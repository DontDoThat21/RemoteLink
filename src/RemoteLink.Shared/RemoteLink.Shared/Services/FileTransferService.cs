namespace RemoteLink.Shared.Services;

using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using System.Collections.Concurrent;

/// <summary>
/// Manages file transfers with chunked streaming and progress tracking.
/// </summary>
public class FileTransferService : IFileTransferService
{
    private readonly ILogger<FileTransferService> _logger;
    private readonly ICommunicationService _communicationService;
    private readonly ConcurrentDictionary<string, TransferState> _activeTransfers = new();
    private const int ChunkSize = 64 * 1024; // 64 KB chunks
    private const long MaxFileSize = 2L * 1024 * 1024 * 1024; // 2 GB limit

    public event EventHandler<FileTransferRequest>? TransferRequested;
    public event EventHandler<FileTransferResponse>? TransferResponseReceived;
    public event EventHandler<FileTransferProgress>? ProgressUpdated;
    public event EventHandler<FileTransferComplete>? TransferCompleted;
    public event EventHandler<FileTransferChunk>? ChunkReceived;

    public FileTransferService(ILogger<FileTransferService> logger, ICommunicationService communicationService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _communicationService = communicationService ?? throw new ArgumentNullException(nameof(communicationService));

        // Wire communication service events
        _communicationService.FileTransferRequestReceived += OnFileTransferRequestReceived;
        _communicationService.FileTransferResponseReceived += OnFileTransferResponseReceived;
        _communicationService.FileTransferChunkReceived += OnFileTransferChunkReceived;
        _communicationService.FileTransferCompleteReceived += OnFileTransferCompleteReceived;
    }

    public async Task<string> InitiateTransferAsync(string filePath, FileTransferDirection direction, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > MaxFileSize)
        {
            _logger.LogWarning("File {FileName} exceeds max size ({Size} > {MaxSize})", fileInfo.Name, fileInfo.Length, MaxFileSize);
            throw new InvalidOperationException($"File size {fileInfo.Length} exceeds maximum {MaxFileSize} bytes.");
        }

        var transferId = Guid.NewGuid().ToString();
        var request = new FileTransferRequest
        {
            TransferId = transferId,
            FileName = fileInfo.Name,
            FileSize = fileInfo.Length,
            MimeType = GetMimeType(fileInfo.Extension),
            Direction = direction,
            Timestamp = DateTime.UtcNow
        };

        var state = new TransferState
        {
            TransferId = transferId,
            LocalPath = filePath,
            TotalBytes = fileInfo.Length,
            Direction = direction,
            StartTime = DateTime.UtcNow
        };

        _activeTransfers[transferId] = state;

        await _communicationService.SendFileTransferRequestAsync(request);
        _logger.LogInformation("Initiated {Direction} transfer {TransferId} for {FileName} ({Size} bytes)", direction, transferId, fileInfo.Name, fileInfo.Length);

        return transferId;
    }

    public async Task<bool> AcceptTransferAsync(string transferId, string savePath)
    {
        if (string.IsNullOrWhiteSpace(transferId))
            throw new ArgumentException("Transfer ID cannot be null or empty.", nameof(transferId));

        if (string.IsNullOrWhiteSpace(savePath))
            throw new ArgumentException("Save path cannot be null or empty.", nameof(savePath));

        var response = new FileTransferResponse
        {
            TransferId = transferId,
            Accepted = true
        };

        // Create transfer state for receiving
        var state = new TransferState
        {
            TransferId = transferId,
            LocalPath = savePath,
            Direction = FileTransferDirection.Download,
            StartTime = DateTime.UtcNow
        };

        _activeTransfers[transferId] = state;

        await _communicationService.SendFileTransferResponseAsync(response);
        _logger.LogInformation("Accepted transfer {TransferId}, will save to {Path}", transferId, savePath);

        return true;
    }

    public async Task<bool> RejectTransferAsync(string transferId, FileTransferRejectionReason reason)
    {
        if (string.IsNullOrWhiteSpace(transferId))
            throw new ArgumentException("Transfer ID cannot be null or empty.", nameof(transferId));

        var response = new FileTransferResponse
        {
            TransferId = transferId,
            Accepted = false,
            RejectionReason = reason,
            Message = reason.ToString()
        };

        await _communicationService.SendFileTransferResponseAsync(response);
        _logger.LogInformation("Rejected transfer {TransferId}: {Reason}", transferId, reason);

        return true;
    }

    public async Task<bool> CancelTransferAsync(string transferId)
    {
        if (!_activeTransfers.TryRemove(transferId, out var state))
        {
            _logger.LogWarning("Cannot cancel transfer {TransferId}: not found", transferId);
            return false;
        }

        state.CancellationTokenSource.Cancel();

        var complete = new FileTransferComplete
        {
            TransferId = transferId,
            Success = false,
            ErrorMessage = "Transfer cancelled by user"
        };

        await _communicationService.SendFileTransferCompleteAsync(complete);
        _logger.LogInformation("Cancelled transfer {TransferId}", transferId);

        TransferCompleted?.Invoke(this, complete);
        return true;
    }

    public FileTransferProgress? GetProgress(string transferId)
    {
        if (!_activeTransfers.TryGetValue(transferId, out var state))
            return null;

        return new FileTransferProgress
        {
            TransferId = transferId,
            BytesTransferred = state.BytesTransferred,
            TotalBytes = state.TotalBytes,
            BytesPerSecond = state.CalculateBytesPerSecond()
        };
    }

    public IReadOnlyList<string> GetActiveTransfers()
    {
        return _activeTransfers.Keys.ToList();
    }

    private void OnFileTransferRequestReceived(object? sender, FileTransferRequest request)
    {
        _logger.LogInformation("Received transfer request {TransferId} for {FileName} ({Size} bytes)", request.TransferId, request.FileName, request.FileSize);
        TransferRequested?.Invoke(this, request);
    }

    private async void OnFileTransferResponseReceived(object? sender, FileTransferResponse response)
    {
        _logger.LogInformation("Received transfer response {TransferId}: Accepted={Accepted}", response.TransferId, response.Accepted);
        TransferResponseReceived?.Invoke(this, response);

        if (response.Accepted && _activeTransfers.TryGetValue(response.TransferId, out var state))
        {
            // Start sending chunks
            await SendFileChunksAsync(state);
        }
        else if (!response.Accepted)
        {
            _activeTransfers.TryRemove(response.TransferId, out _);
            var complete = new FileTransferComplete
            {
                TransferId = response.TransferId,
                Success = false,
                ErrorMessage = response.Message ?? response.RejectionReason?.ToString() ?? "Transfer rejected"
            };
            TransferCompleted?.Invoke(this, complete);
        }
    }

    private async void OnFileTransferChunkReceived(object? sender, FileTransferChunk chunk)
    {
        ChunkReceived?.Invoke(this, chunk);

        if (!_activeTransfers.TryGetValue(chunk.TransferId, out var state))
        {
            _logger.LogWarning("Received chunk for unknown transfer {TransferId}", chunk.TransferId);
            return;
        }

        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(state.LocalPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Append chunk to file
            using (var fs = new FileStream(state.LocalPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            {
                fs.Seek(chunk.Offset, SeekOrigin.Begin);
                await fs.WriteAsync(chunk.Data, 0, chunk.Length);
            }

            state.BytesTransferred += chunk.Length;
            state.TotalBytes = Math.Max(state.TotalBytes, chunk.Offset + chunk.Length);

            var progress = new FileTransferProgress
            {
                TransferId = chunk.TransferId,
                BytesTransferred = state.BytesTransferred,
                TotalBytes = state.TotalBytes,
                BytesPerSecond = state.CalculateBytesPerSecond()
            };

            ProgressUpdated?.Invoke(this, progress);

            if (chunk.IsLastChunk)
            {
                _activeTransfers.TryRemove(chunk.TransferId, out _);

                var complete = new FileTransferComplete
                {
                    TransferId = chunk.TransferId,
                    Success = true,
                    SavedPath = state.LocalPath
                };

                await _communicationService.SendFileTransferCompleteAsync(complete);
                TransferCompleted?.Invoke(this, complete);

                _logger.LogInformation("Transfer {TransferId} completed successfully, saved to {Path}", chunk.TransferId, state.LocalPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing chunk for transfer {TransferId}", chunk.TransferId);

            _activeTransfers.TryRemove(chunk.TransferId, out _);

            var complete = new FileTransferComplete
            {
                TransferId = chunk.TransferId,
                Success = false,
                ErrorMessage = ex.Message
            };

            await _communicationService.SendFileTransferCompleteAsync(complete);
            TransferCompleted?.Invoke(this, complete);
        }
    }

    private void OnFileTransferCompleteReceived(object? sender, FileTransferComplete complete)
    {
        _logger.LogInformation("Transfer {TransferId} completed: Success={Success}", complete.TransferId, complete.Success);
        _activeTransfers.TryRemove(complete.TransferId, out _);
        TransferCompleted?.Invoke(this, complete);
    }

    private async Task SendFileChunksAsync(TransferState state)
    {
        try
        {
            using var fs = new FileStream(state.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[ChunkSize];
            long offset = 0;

            while (offset < fs.Length && !state.CancellationTokenSource.Token.IsCancellationRequested)
            {
                int bytesRead = await fs.ReadAsync(buffer, 0, ChunkSize, state.CancellationTokenSource.Token);
                if (bytesRead == 0) break;

                var chunk = new FileTransferChunk
                {
                    TransferId = state.TransferId,
                    Offset = offset,
                    Length = bytesRead,
                    Data = buffer.Take(bytesRead).ToArray(),
                    IsLastChunk = (offset + bytesRead >= fs.Length)
                };

                await _communicationService.SendFileTransferChunkAsync(chunk);

                state.BytesTransferred += bytesRead;
                offset += bytesRead;

                var progress = new FileTransferProgress
                {
                    TransferId = state.TransferId,
                    BytesTransferred = state.BytesTransferred,
                    TotalBytes = state.TotalBytes,
                    BytesPerSecond = state.CalculateBytesPerSecond()
                };

                ProgressUpdated?.Invoke(this, progress);

                // Small delay to avoid flooding
                await Task.Delay(10, state.CancellationTokenSource.Token);
            }

            _logger.LogInformation("Finished sending {BytesTransferred} bytes for transfer {TransferId}", state.BytesTransferred, state.TransferId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending chunks for transfer {TransferId}", state.TransferId);

            var complete = new FileTransferComplete
            {
                TransferId = state.TransferId,
                Success = false,
                ErrorMessage = ex.Message
            };

            await _communicationService.SendFileTransferCompleteAsync(complete);
            TransferCompleted?.Invoke(this, complete);
        }
    }

    private static string GetMimeType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".txt" => "text/plain",
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".zip" => "application/zip",
            ".mp4" => "video/mp4",
            ".mp3" => "audio/mpeg",
            ".doc" or ".docx" => "application/msword",
            ".xls" or ".xlsx" => "application/vnd.ms-excel",
            _ => "application/octet-stream"
        };
    }

    private class TransferState
    {
        public string TransferId { get; set; } = string.Empty;
        public string LocalPath { get; set; } = string.Empty;
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public FileTransferDirection Direction { get; set; }
        public DateTime StartTime { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; set; } = new();

        public long CalculateBytesPerSecond()
        {
            var elapsed = DateTime.UtcNow - StartTime;
            if (elapsed.TotalSeconds < 1) return 0;
            return (long)(BytesTransferred / elapsed.TotalSeconds);
        }
    }
}
