using Microsoft.Extensions.Logging;
using RemoteLink.Desktop.Services;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Tests;

public class PrintServiceTests
{
    private readonly MockPrintService _printService;
    private readonly ILogger<MockPrintService> _logger;

    public PrintServiceTests()
    {
        _logger = LoggerFactory.Create(builder => builder.AddConsole())
            .CreateLogger<MockPrintService>();
        _printService = new MockPrintService(_logger);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MockPrintService(null!));
    }

    [Fact]
    public async Task GetAvailablePrintersAsync_ReturnsAtLeastOnePrinter()
    {
        // Act
        var printers = await _printService.GetAvailablePrintersAsync();

        // Assert
        Assert.NotNull(printers);
        Assert.NotEmpty(printers);
    }

    [Fact]
    public async Task GetAvailablePrintersAsync_DefaultPrinterExists()
    {
        // Act
        var printers = await _printService.GetAvailablePrintersAsync();

        // Assert
        Assert.Contains(printers, p => p.IsDefault);
    }

    [Fact]
    public async Task GetAvailablePrintersAsync_AllPrintersHaveNames()
    {
        // Act
        var printers = await _printService.GetAvailablePrintersAsync();

        // Assert
        Assert.All(printers, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));
    }

    [Fact]
    public async Task GetAvailablePrintersAsync_AllPrintersHaveSupportedMimeTypes()
    {
        // Act
        var printers = await _printService.GetAvailablePrintersAsync();

        // Assert
        Assert.All(printers, p =>
        {
            Assert.NotNull(p.SupportedMimeTypes);
            Assert.NotEmpty(p.SupportedMimeTypes);
        });
    }

    [Fact]
    public async Task SubmitPrintJobAsync_NullJob_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _printService.SubmitPrintJobAsync(null!));
    }

    [Fact]
    public async Task SubmitPrintJobAsync_ValidJob_ReturnsTrue()
    {
        // Arrange
        var job = CreateTestPrintJob();

        // Act
        var result = await _printService.SubmitPrintJobAsync(job);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task SubmitPrintJobAsync_ValidJob_FiresStatusChangedEvents()
    {
        // Arrange
        var job = CreateTestPrintJob();
        var statusUpdates = new List<PrintJobStatus>();
        
        _printService.StatusChanged += (sender, e) => statusUpdates.Add(e.Status);

        // Act
        await _printService.SubmitPrintJobAsync(job);
        await Task.Delay(800); // Wait for mock printing to complete

        // Assert
        Assert.NotEmpty(statusUpdates);
        Assert.Contains(statusUpdates, s => s.State == PrintJobState.Queued);
        Assert.Contains(statusUpdates, s => s.State == PrintJobState.Printing);
        Assert.Contains(statusUpdates, s => s.State == PrintJobState.Completed);
    }

    [Fact]
    public async Task SubmitPrintJobAsync_ValidJob_StatusesHaveCorrectJobId()
    {
        // Arrange
        var job = CreateTestPrintJob();
        var statusUpdates = new List<PrintJobStatus>();
        
        _printService.StatusChanged += (sender, e) => statusUpdates.Add(e.Status);

        // Act
        await _printService.SubmitPrintJobAsync(job);
        await Task.Delay(800);

        // Assert
        Assert.All(statusUpdates, s => Assert.Equal(job.JobId, s.JobId));
    }

    [Fact]
    public async Task CancelPrintJobAsync_NullJobId_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _printService.CancelPrintJobAsync(null!));
    }

    [Fact]
    public async Task CancelPrintJobAsync_EmptyJobId_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _printService.CancelPrintJobAsync(string.Empty));
    }

    [Fact]
    public async Task CancelPrintJobAsync_WhitespaceJobId_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _printService.CancelPrintJobAsync("   "));
    }

    [Fact]
    public async Task CancelPrintJobAsync_UnknownJob_ReturnsFalse()
    {
        // Act
        var result = await _printService.CancelPrintJobAsync("unknown-job-id");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CancelPrintJobAsync_QueuedJob_ReturnsTrue()
    {
        // Arrange
        var job = CreateTestPrintJob();
        await _printService.SubmitPrintJobAsync(job);
        // Don't wait, so it's still queued/printing

        // Act
        var result = await _printService.CancelPrintJobAsync(job.JobId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CancelPrintJobAsync_QueuedJob_FiresCancelledStatus()
    {
        // Arrange
        var job = CreateTestPrintJob();
        var statusUpdates = new List<PrintJobStatus>();
        
        _printService.StatusChanged += (sender, e) => statusUpdates.Add(e.Status);
        
        await _printService.SubmitPrintJobAsync(job);

        // Act
        await _printService.CancelPrintJobAsync(job.JobId);

        // Assert
        Assert.Contains(statusUpdates, s => s.State == PrintJobState.Cancelled);
    }

    [Fact]
    public async Task GetJobStatusAsync_NullJobId_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _printService.GetJobStatusAsync(null!));
    }

    [Fact]
    public async Task GetJobStatusAsync_EmptyJobId_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _printService.GetJobStatusAsync(string.Empty));
    }

    [Fact]
    public async Task GetJobStatusAsync_WhitespaceJobId_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _printService.GetJobStatusAsync("   "));
    }

    [Fact]
    public async Task GetJobStatusAsync_UnknownJob_ReturnsNull()
    {
        // Act
        var status = await _printService.GetJobStatusAsync("unknown-job-id");

        // Assert
        Assert.Null(status);
    }

    [Fact]
    public async Task GetJobStatusAsync_SubmittedJob_ReturnsNonNull()
    {
        // Arrange
        var job = CreateTestPrintJob();
        await _printService.SubmitPrintJobAsync(job);

        // Act
        var status = await _printService.GetJobStatusAsync(job.JobId);

        // Assert
        Assert.NotNull(status);
        Assert.Equal(job.JobId, status.JobId);
    }

    [Fact]
    public async Task GetJobStatusAsync_CompletedJob_ReturnsCompletedState()
    {
        // Arrange
        var job = CreateTestPrintJob();
        await _printService.SubmitPrintJobAsync(job);
        await Task.Delay(800); // Wait for completion

        // Act
        var status = await _printService.GetJobStatusAsync(job.JobId);

        // Assert
        Assert.NotNull(status);
        Assert.Equal(PrintJobState.Completed, status.State);
    }

    [Fact]
    public async Task PrintJob_WithMultipleCopies_AcceptsCopiesParameter()
    {
        // Arrange
        var job = CreateTestPrintJob(copies: 3);

        // Act
        var result = await _printService.SubmitPrintJobAsync(job);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task PrintJob_WithDuplex_AcceptsDuplexParameter()
    {
        // Arrange
        var job = CreateTestPrintJob(duplex: true);

        // Act
        var result = await _printService.SubmitPrintJobAsync(job);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task PrintJob_WithGrayscale_AcceptsColorParameter()
    {
        // Arrange
        var job = CreateTestPrintJob(color: false);

        // Act
        var result = await _printService.SubmitPrintJobAsync(job);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task PrintJob_WithSpecificPrinter_AcceptsPrinterName()
    {
        // Arrange
        var job = CreateTestPrintJob(printerName: "Specific Printer");

        // Act
        var result = await _printService.SubmitPrintJobAsync(job);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task PrintJob_ImagePng_AcceptedMimeType()
    {
        // Arrange
        var job = CreateTestPrintJob(mimeType: "image/png");

        // Act
        var result = await _printService.SubmitPrintJobAsync(job);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task PrintJob_ImageJpeg_AcceptedMimeType()
    {
        // Arrange
        var job = CreateTestPrintJob(mimeType: "image/jpeg");

        // Act
        var result = await _printService.SubmitPrintJobAsync(job);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task PrintJob_TextPlain_AcceptedMimeType()
    {
        // Arrange
        var job = CreateTestPrintJob(mimeType: "text/plain");

        // Act
        var result = await _printService.SubmitPrintJobAsync(job);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task PrintJob_MultipleConcurrentJobs_AllComplete()
    {
        // Arrange
        var job1 = CreateTestPrintJob(jobId: "job-1");
        var job2 = CreateTestPrintJob(jobId: "job-2");
        var job3 = CreateTestPrintJob(jobId: "job-3");

        // Act
        var result1 = await _printService.SubmitPrintJobAsync(job1);
        var result2 = await _printService.SubmitPrintJobAsync(job2);
        var result3 = await _printService.SubmitPrintJobAsync(job3);
        
        await Task.Delay(1000); // Wait for all to complete

        // Assert
        Assert.True(result1);
        Assert.True(result2);
        Assert.True(result3);

        var status1 = await _printService.GetJobStatusAsync("job-1");
        var status2 = await _printService.GetJobStatusAsync("job-2");
        var status3 = await _printService.GetJobStatusAsync("job-3");

        Assert.Equal(PrintJobState.Completed, status1?.State);
        Assert.Equal(PrintJobState.Completed, status2?.State);
        Assert.Equal(PrintJobState.Completed, status3?.State);
    }

    private PrintJob CreateTestPrintJob(
        string? jobId = null,
        string? documentName = null,
        string? mimeType = null,
        int copies = 1,
        bool color = true,
        bool duplex = false,
        string? printerName = null)
    {
        return new PrintJob
        {
            JobId = jobId ?? Guid.NewGuid().ToString(),
            DocumentName = documentName ?? "Test Document",
            Data = new byte[] { 0x89, 0x50, 0x4E, 0x47 }, // Fake PNG header
            MimeType = mimeType ?? "image/png",
            Copies = copies,
            Color = color,
            Duplex = duplex,
            PrinterName = printerName
        };
    }
}
