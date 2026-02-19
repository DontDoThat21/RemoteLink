using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Mock implementation of <see cref="IPrintService"/> for testing and non-Windows platforms.
/// Simulates print operations by logging them without actual printing.
/// </summary>
public class MockPrintService : IPrintService
{
    private readonly ILogger<MockPrintService> _logger;
    private readonly ConcurrentDictionary<string, PrintJobState> _jobStates = new();

    public MockPrintService(ILogger<MockPrintService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public event EventHandler<PrintJobStatusEventArgs>? StatusChanged;

    /// <inheritdoc/>
    public Task<IReadOnlyList<PrinterInfo>> GetAvailablePrintersAsync()
    {
        _logger.LogInformation("[Mock] GetAvailablePrintersAsync called");
        
        // Return a fake printer
        var printers = new List<PrinterInfo>
        {
            new PrinterInfo
            {
                Name = "Mock Printer",
                IsDefault = true,
                IsOnline = true,
                SupportedMimeTypes = new[] 
                { 
                    "image/png", 
                    "image/jpeg", 
                    "image/bmp",
                    "text/plain",
                    "application/pdf"
                }
            }
        };

        return Task.FromResult<IReadOnlyList<PrinterInfo>>(printers);
    }

    /// <inheritdoc/>
    public async Task<bool> SubmitPrintJobAsync(PrintJob printJob)
    {
        ArgumentNullException.ThrowIfNull(printJob);

        _logger.LogInformation(
            "[Mock] Submitting print job: {JobId} - {DocumentName} ({Size} bytes, {MimeType}, {Copies} copies)",
            printJob.JobId,
            printJob.DocumentName,
            printJob.Data.Length,
            printJob.MimeType,
            printJob.Copies);

        _jobStates[printJob.JobId] = PrintJobState.Queued;
        OnStatusChanged(new PrintJobStatus
        {
            JobId = printJob.JobId,
            State = PrintJobState.Queued,
            PagesPrinted = 0,
            TotalPages = 1
        });

        // Simulate async printing
        await Task.Delay(100);

        _jobStates[printJob.JobId] = PrintJobState.Printing;
        OnStatusChanged(new PrintJobStatus
        {
            JobId = printJob.JobId,
            State = PrintJobState.Printing,
            PagesPrinted = 0,
            TotalPages = 1
        });

        await Task.Delay(500);

        _jobStates[printJob.JobId] = PrintJobState.Completed;
        OnStatusChanged(new PrintJobStatus
        {
            JobId = printJob.JobId,
            State = PrintJobState.Completed,
            PagesPrinted = 1,
            TotalPages = 1
        });

        _logger.LogInformation("[Mock] Print job completed: {JobId}", printJob.JobId);
        return true;
    }

    /// <inheritdoc/>
    public Task<bool> CancelPrintJobAsync(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("Job ID cannot be null or empty", nameof(jobId));

        _logger.LogInformation("[Mock] Cancelling print job: {JobId}", jobId);

        if (_jobStates.TryGetValue(jobId, out var state) && 
            (state == PrintJobState.Queued || state == PrintJobState.Printing))
        {
            _jobStates[jobId] = PrintJobState.Cancelled;
            OnStatusChanged(new PrintJobStatus
            {
                JobId = jobId,
                State = PrintJobState.Cancelled
            });
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public Task<PrintJobStatus?> GetJobStatusAsync(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("Job ID cannot be null or empty", nameof(jobId));

        if (_jobStates.TryGetValue(jobId, out var state))
        {
            return Task.FromResult<PrintJobStatus?>(new PrintJobStatus
            {
                JobId = jobId,
                State = state
            });
        }

        return Task.FromResult<PrintJobStatus?>(null);
    }

    private void OnStatusChanged(PrintJobStatus status)
    {
        StatusChanged?.Invoke(this, new PrintJobStatusEventArgs { Status = status });
    }
}
