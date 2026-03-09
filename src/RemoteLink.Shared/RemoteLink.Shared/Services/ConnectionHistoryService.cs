using System.Text.Json;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// JSON-file-backed implementation of <see cref="IConnectionHistoryService"/>.
/// Data is stored at <c>%AppData%\RemoteLink\connection_history.json</c>.
/// Keeps at most 100 records, trimming the oldest on overflow.
/// </summary>
public sealed class ConnectionHistoryService : IConnectionHistoryService
{
    private const int MaxRecords = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly ILogger<ConnectionHistoryService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<ConnectionRecord> _records = new();

    public ConnectionHistoryService(ILogger<ConnectionHistoryService> logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _filePath = Path.Combine(appData, "RemoteLink", "connection_history.json");
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("Connection history file not found at {Path}; starting empty", _filePath);
                _records = new List<ConnectionRecord>();
                return;
            }

            var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            _records = JsonSerializer.Deserialize<List<ConnectionRecord>>(json, JsonOptions) ?? new List<ConnectionRecord>();
            _logger.LogInformation("Loaded {Count} connection record(s) from {Path}", _records.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load connection history from {Path}", _filePath);
            _records = new List<ConnectionRecord>();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_records, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Saved {Count} connection record(s) to {Path}", _records.Count, _filePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    public IReadOnlyList<ConnectionRecord> GetAll()
    {
        return _records
            .OrderByDescending(r => r.ConnectedAt)
            .ToList()
            .AsReadOnly();
    }

    public async Task AddAsync(ConnectionRecord record, CancellationToken cancellationToken = default)
    {
        _records.Add(record);

        // Trim oldest records if over capacity
        if (_records.Count > MaxRecords)
        {
            _records = _records
                .OrderByDescending(r => r.ConnectedAt)
                .Take(MaxRecords)
                .ToList();
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<ConnectionRecord> GetRecent(int count)
    {
        return _records
            .OrderByDescending(r => r.ConnectedAt)
            .Take(count)
            .ToList()
            .AsReadOnly();
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _records.Clear();
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Connection history cleared");
    }
}
