using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Service for managing remote printing operations.
/// </summary>
public interface IPrintService
{
    /// <summary>
    /// Get a list of available printers on this device.
    /// </summary>
    Task<IReadOnlyList<PrinterInfo>> GetAvailablePrintersAsync();

    /// <summary>
    /// Submit a print job to a local printer.
    /// </summary>
    /// <param name="printJob">The print job to execute.</param>
    /// <returns>True if the job was submitted successfully.</returns>
    Task<bool> SubmitPrintJobAsync(PrintJob printJob);

    /// <summary>
    /// Cancel a pending or in-progress print job.
    /// </summary>
    /// <param name="jobId">ID of the job to cancel.</param>
    /// <returns>True if the job was cancelled.</returns>
    Task<bool> CancelPrintJobAsync(string jobId);

    /// <summary>
    /// Get the current status of a print job.
    /// </summary>
    /// <param name="jobId">ID of the job to check.</param>
    /// <returns>Current status, or null if job not found.</returns>
    Task<PrintJobStatus?> GetJobStatusAsync(string jobId);

    /// <summary>
    /// Event fired when a print job status changes.
    /// </summary>
    event EventHandler<PrintJobStatusEventArgs>? StatusChanged;
}

/// <summary>
/// Event args for print job status changes.
/// </summary>
public class PrintJobStatusEventArgs : EventArgs
{
    public required PrintJobStatus Status { get; init; }
}
