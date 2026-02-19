namespace RemoteLink.Shared.Models;

/// <summary>
/// Represents a print job to be sent to a remote printer.
/// </summary>
public class PrintJob
{
    /// <summary>
    /// Unique identifier for the print job.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Document name/title.
    /// </summary>
    public required string DocumentName { get; init; }

    /// <summary>
    /// Raw print data (PDF, PostScript, or image bytes).
    /// </summary>
    public required byte[] Data { get; init; }

    /// <summary>
    /// MIME type of the print data (application/pdf, image/png, etc.).
    /// </summary>
    public required string MimeType { get; init; }

    /// <summary>
    /// Number of copies to print.
    /// </summary>
    public int Copies { get; init; } = 1;

    /// <summary>
    /// Whether to print in color (true) or grayscale (false).
    /// </summary>
    public bool Color { get; init; } = true;

    /// <summary>
    /// Print on both sides of the paper.
    /// </summary>
    public bool Duplex { get; init; } = false;

    /// <summary>
    /// Target printer name (null = default printer).
    /// </summary>
    public string? PrinterName { get; init; }

    /// <summary>
    /// Timestamp when the job was created.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Response to a print job request.
/// </summary>
public class PrintJobResponse
{
    /// <summary>
    /// Job ID this response corresponds to.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Whether the job was accepted for printing.
    /// </summary>
    public bool Accepted { get; init; }

    /// <summary>
    /// Reason for rejection (if not accepted).
    /// </summary>
    public PrintJobRejectionReason? RejectionReason { get; init; }

    /// <summary>
    /// Human-readable message.
    /// </summary>
    public string? Message { get; init; }
}

/// <summary>
/// Status update for a print job.
/// </summary>
public class PrintJobStatus
{
    /// <summary>
    /// Job ID this status corresponds to.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Current state of the print job.
    /// </summary>
    public required PrintJobState State { get; init; }

    /// <summary>
    /// Number of pages printed so far.
    /// </summary>
    public int PagesPrinted { get; init; }

    /// <summary>
    /// Total number of pages in the job.
    /// </summary>
    public int TotalPages { get; init; }

    /// <summary>
    /// Error message if State is Error or Failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Timestamp of this status update.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Information about an available printer.
/// </summary>
public class PrinterInfo
{
    /// <summary>
    /// Printer name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Whether this is the default printer.
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// Whether the printer is currently online.
    /// </summary>
    public bool IsOnline { get; init; }

    /// <summary>
    /// Supported MIME types (e.g., application/pdf, image/png).
    /// </summary>
    public required string[] SupportedMimeTypes { get; init; }
}

/// <summary>
/// Reasons a print job might be rejected.
/// </summary>
public enum PrintJobRejectionReason
{
    /// <summary>
    /// Printer not found or unavailable.
    /// </summary>
    PrinterNotAvailable,

    /// <summary>
    /// Unsupported file format.
    /// </summary>
    UnsupportedFormat,

    /// <summary>
    /// Printer is offline or in error state.
    /// </summary>
    PrinterOffline,

    /// <summary>
    /// User declined the print request.
    /// </summary>
    UserDeclined,

    /// <summary>
    /// Print data too large.
    /// </summary>
    DataTooLarge,

    /// <summary>
    /// Generic error.
    /// </summary>
    Error
}

/// <summary>
/// States a print job can be in.
/// </summary>
public enum PrintJobState
{
    /// <summary>
    /// Job is queued for printing.
    /// </summary>
    Queued,

    /// <summary>
    /// Job is currently printing.
    /// </summary>
    Printing,

    /// <summary>
    /// Job completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Job failed with an error.
    /// </summary>
    Failed,

    /// <summary>
    /// Job was cancelled.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Printer is paused.
    /// </summary>
    Paused,

    /// <summary>
    /// Printer error (out of paper, jam, etc.).
    /// </summary>
    Error
}
