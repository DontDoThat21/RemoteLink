namespace RemoteLink.Shared.Models;

/// <summary>
/// Represents a file transfer request between client and host.
/// </summary>
public class FileTransferRequest
{
    /// <summary>
    /// Unique identifier for this file transfer operation.
    /// </summary>
    public string TransferId { get; set; } = string.Empty;

    /// <summary>
    /// Name of the file being transferred (without path).
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Total size of the file in bytes.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// MIME type of the file (e.g., "text/plain", "application/pdf").
    /// </summary>
    public string MimeType { get; set; } = "application/octet-stream";

    /// <summary>
    /// Direction of the transfer.
    /// </summary>
    public FileTransferDirection Direction { get; set; }

    /// <summary>
    /// Timestamp when the transfer was initiated.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a chunk of file data being transferred.
/// </summary>
public class FileTransferChunk
{
    /// <summary>
    /// Transfer ID this chunk belongs to.
    /// </summary>
    public string TransferId { get; set; } = string.Empty;

    /// <summary>
    /// Offset position in the file (in bytes).
    /// </summary>
    public long Offset { get; set; }

    /// <summary>
    /// Length of data in this chunk.
    /// </summary>
    public int Length { get; set; }

    /// <summary>
    /// Raw chunk data.
    /// </summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Whether this is the last chunk in the transfer.
    /// </summary>
    public bool IsLastChunk { get; set; }
}

/// <summary>
/// Progress information for an ongoing file transfer.
/// </summary>
public class FileTransferProgress
{
    /// <summary>
    /// Transfer ID.
    /// </summary>
    public string TransferId { get; set; } = string.Empty;

    /// <summary>
    /// Number of bytes transferred so far.
    /// </summary>
    public long BytesTransferred { get; set; }

    /// <summary>
    /// Total file size in bytes.
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Transfer speed in bytes per second.
    /// </summary>
    public long BytesPerSecond { get; set; }

    /// <summary>
    /// Percentage complete (0-100).
    /// </summary>
    public double PercentComplete => TotalBytes > 0 ? (BytesTransferred * 100.0 / TotalBytes) : 0;

    /// <summary>
    /// Estimated time remaining (null if unknown).
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining
    {
        get
        {
            if (BytesPerSecond <= 0 || TotalBytes <= 0) return null;
            long remainingBytes = TotalBytes - BytesTransferred;
            return TimeSpan.FromSeconds(remainingBytes / (double)BytesPerSecond);
        }
    }
}

/// <summary>
/// Response to a file transfer request.
/// </summary>
public class FileTransferResponse
{
    /// <summary>
    /// Transfer ID from the original request.
    /// </summary>
    public string TransferId { get; set; } = string.Empty;

    /// <summary>
    /// Whether the transfer was accepted.
    /// </summary>
    public bool Accepted { get; set; }

    /// <summary>
    /// Reason for rejection (if Accepted = false).
    /// </summary>
    public FileTransferRejectionReason? RejectionReason { get; set; }

    /// <summary>
    /// Optional message (e.g., error details).
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
/// Notification that a file transfer has completed or failed.
/// </summary>
public class FileTransferComplete
{
    /// <summary>
    /// Transfer ID.
    /// </summary>
    public string TransferId { get; set; } = string.Empty;

    /// <summary>
    /// Whether the transfer succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message (if Success = false).
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Local path where the file was saved (if Success = true).
    /// </summary>
    public string? SavedPath { get; set; }
}

/// <summary>
/// Direction of a file transfer.
/// </summary>
public enum FileTransferDirection
{
    /// <summary>
    /// File is being uploaded from client to host.
    /// </summary>
    Upload,

    /// <summary>
    /// File is being downloaded from host to client.
    /// </summary>
    Download
}

/// <summary>
/// Reasons for rejecting a file transfer.
/// </summary>
public enum FileTransferRejectionReason
{
    /// <summary>
    /// File size exceeds the configured maximum.
    /// </summary>
    FileTooLarge,

    /// <summary>
    /// File type is not allowed.
    /// </summary>
    FileTypeNotAllowed,

    /// <summary>
    /// Insufficient disk space.
    /// </summary>
    InsufficientDiskSpace,

    /// <summary>
    /// User declined the transfer.
    /// </summary>
    UserDeclined,

    /// <summary>
    /// Destination path is invalid or inaccessible.
    /// </summary>
    InvalidPath,

    /// <summary>
    /// Generic error.
    /// </summary>
    Error
}
