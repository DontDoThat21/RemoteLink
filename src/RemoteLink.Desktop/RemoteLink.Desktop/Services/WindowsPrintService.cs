using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Printing;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Windows-specific implementation of <see cref="IPrintService"/> using System.Drawing.Printing.
/// Supports printing images (PNG, JPEG, BMP) and text documents.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsPrintService : IPrintService
{
    private readonly ILogger<WindowsPrintService> _logger;
    private readonly ConcurrentDictionary<string, PrintJobState> _jobStates = new();
    
    public WindowsPrintService(ILogger<WindowsPrintService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public event EventHandler<PrintJobStatusEventArgs>? StatusChanged;

    /// <inheritdoc/>
    public Task<IReadOnlyList<PrinterInfo>> GetAvailablePrintersAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("GetAvailablePrintersAsync called on non-Windows platform");
            return Task.FromResult<IReadOnlyList<PrinterInfo>>(Array.Empty<PrinterInfo>());
        }

        var printers = new List<PrinterInfo>();
        
        try
        {
            var defaultPrinter = new PrinterSettings().PrinterName;
            
            foreach (string printerName in PrinterSettings.InstalledPrinters)
            {
                try
                {
                    var settings = new PrinterSettings { PrinterName = printerName };
                    
                    printers.Add(new PrinterInfo
                    {
                        Name = printerName,
                        IsDefault = printerName == defaultPrinter,
                        IsOnline = settings.IsValid,
                        SupportedMimeTypes = new[] 
                        { 
                            "image/png", 
                            "image/jpeg", 
                            "image/bmp",
                            "text/plain"
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to query printer: {PrinterName}", printerName);
                }
            }
            
            _logger.LogInformation("Found {Count} available printers", printers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate printers");
        }
        
        return Task.FromResult<IReadOnlyList<PrinterInfo>>(printers);
    }

    /// <inheritdoc/>
    public Task<bool> SubmitPrintJobAsync(PrintJob printJob)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("SubmitPrintJobAsync called on non-Windows platform");
            return Task.FromResult(false);
        }

        ArgumentNullException.ThrowIfNull(printJob);

        try
        {
            _logger.LogInformation("Submitting print job: {JobId} - {DocumentName}", printJob.JobId, printJob.DocumentName);
            
            // Track job state
            _jobStates[printJob.JobId] = PrintJobState.Queued;
            
            // Fire queued status
            OnStatusChanged(new PrintJobStatus
            {
                JobId = printJob.JobId,
                State = PrintJobState.Queued,
                PagesPrinted = 0,
                TotalPages = 1
            });

            // Execute print on background thread to avoid blocking
            _ = Task.Run(() => ExecutePrintJob(printJob));
            
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit print job: {JobId}", printJob.JobId);
            
            _jobStates[printJob.JobId] = PrintJobState.Failed;
            OnStatusChanged(new PrintJobStatus
            {
                JobId = printJob.JobId,
                State = PrintJobState.Failed,
                ErrorMessage = ex.Message
            });
            
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    public Task<bool> CancelPrintJobAsync(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("Job ID cannot be null or empty", nameof(jobId));

        if (_jobStates.TryGetValue(jobId, out var state) && 
            (state == PrintJobState.Queued || state == PrintJobState.Printing))
        {
            _jobStates[jobId] = PrintJobState.Cancelled;
            
            OnStatusChanged(new PrintJobStatus
            {
                JobId = jobId,
                State = PrintJobState.Cancelled
            });
            
            _logger.LogInformation("Print job cancelled: {JobId}", jobId);
            return Task.FromResult(true);
        }

        _logger.LogWarning("Cannot cancel job {JobId} - current state: {State}", jobId, state);
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

    private void ExecutePrintJob(PrintJob printJob)
    {
        try
        {
            // Check if cancelled before starting
            if (_jobStates.TryGetValue(printJob.JobId, out var state) && state == PrintJobState.Cancelled)
            {
                _logger.LogInformation("Print job {JobId} was cancelled before execution", printJob.JobId);
                return;
            }

            _jobStates[printJob.JobId] = PrintJobState.Printing;
            OnStatusChanged(new PrintJobStatus
            {
                JobId = printJob.JobId,
                State = PrintJobState.Printing,
                PagesPrinted = 0,
                TotalPages = 1
            });

            var printDocument = new PrintDocument
            {
                DocumentName = printJob.DocumentName
            };

            // Set printer if specified
            if (!string.IsNullOrWhiteSpace(printJob.PrinterName))
            {
                printDocument.PrinterSettings.PrinterName = printJob.PrinterName;
            }

            // Set copies
            printDocument.PrinterSettings.Copies = (short)Math.Min(printJob.Copies, 99);
            
            // Set duplex
            if (printJob.Duplex)
            {
                printDocument.PrinterSettings.Duplex = Duplex.Vertical;
            }

            // Set color mode
            printDocument.DefaultPageSettings.Color = printJob.Color;

            // Determine print handler based on MIME type
            if (printJob.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                PrintImage(printDocument, printJob);
            }
            else if (printJob.MimeType == "text/plain")
            {
                PrintText(printDocument, printJob);
            }
            else
            {
                _logger.LogWarning("Unsupported MIME type: {MimeType}", printJob.MimeType);
                throw new NotSupportedException($"MIME type {printJob.MimeType} is not supported");
            }

            _jobStates[printJob.JobId] = PrintJobState.Completed;
            OnStatusChanged(new PrintJobStatus
            {
                JobId = printJob.JobId,
                State = PrintJobState.Completed,
                PagesPrinted = 1,
                TotalPages = 1
            });

            _logger.LogInformation("Print job completed: {JobId}", printJob.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Print job failed: {JobId}", printJob.JobId);
            
            _jobStates[printJob.JobId] = PrintJobState.Failed;
            OnStatusChanged(new PrintJobStatus
            {
                JobId = printJob.JobId,
                State = PrintJobState.Failed,
                ErrorMessage = ex.Message
            });
        }
    }

    private void PrintImage(PrintDocument printDocument, PrintJob printJob)
    {
        using var memoryStream = new MemoryStream(printJob.Data);
        using var image = Image.FromStream(memoryStream);
        
        printDocument.PrintPage += (sender, e) =>
        {
            // Check if cancelled during printing
            if (_jobStates.TryGetValue(printJob.JobId, out var state) && state == PrintJobState.Cancelled)
            {
                e.Cancel = true;
                return;
            }

            // Scale image to fit page while maintaining aspect ratio
            var bounds = e.MarginBounds;
            var scale = Math.Min(
                (double)bounds.Width / image.Width,
                (double)bounds.Height / image.Height);
            
            var width = (int)(image.Width * scale);
            var height = (int)(image.Height * scale);
            
            // Center the image
            var x = bounds.Left + (bounds.Width - width) / 2;
            var y = bounds.Top + (bounds.Height - height) / 2;
            
            e.Graphics?.DrawImage(image, x, y, width, height);
            e.HasMorePages = false;
        };

        printDocument.Print();
    }

    private void PrintText(PrintDocument printDocument, PrintJob printJob)
    {
        var text = System.Text.Encoding.UTF8.GetString(printJob.Data);
        var lines = text.Split('\n');
        var lineIndex = 0;

        printDocument.PrintPage += (sender, e) =>
        {
            // Check if cancelled during printing
            if (_jobStates.TryGetValue(printJob.JobId, out var state) && state == PrintJobState.Cancelled)
            {
                e.Cancel = true;
                return;
            }

            var bounds = e.MarginBounds;
            var font = new Font("Courier New", 10);
            var y = bounds.Top;
            var lineHeight = font.GetHeight(e.Graphics!);

            while (lineIndex < lines.Length && y + lineHeight <= bounds.Bottom)
            {
                e.Graphics?.DrawString(lines[lineIndex], font, Brushes.Black, bounds.Left, y);
                y += (int)lineHeight;
                lineIndex++;
            }

            e.HasMorePages = lineIndex < lines.Length;
        };

        printDocument.Print();
    }

    private void OnStatusChanged(PrintJobStatus status)
    {
        StatusChanged?.Invoke(this, new PrintJobStatusEventArgs { Status = status });
    }
}
