using System.Text.Json;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// JSON-file-backed implementation of <see cref="IAppSettingsService"/>.
/// Settings are stored at <c>%AppData%\RemoteLink\settings.json</c>.
/// All public members are thread-safe.
/// </summary>
public sealed class AppSettingsService : IAppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;
    private readonly ILogger<AppSettingsService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private AppSettings _current = new();

    /// <inheritdoc/>
    public AppSettings Current => _current;

    /// <inheritdoc/>
    public event EventHandler? SettingsSaved;

    /// <summary>
    /// Initialises the service.  The settings file path defaults to
    /// <c>%AppData%\RemoteLink\settings.json</c> and is created on first save.
    /// </summary>
    public AppSettingsService(ILogger<AppSettingsService> logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _settingsPath = Path.Combine(appData, "RemoteLink", "settings.json");
    }

    /// <inheritdoc/>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_settingsPath))
            {
                _logger.LogInformation("Settings file not found at {Path}; using defaults", _settingsPath);
                _current = new AppSettings();
                await SaveInternalAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            var json = await File.ReadAllTextAsync(_settingsPath, cancellationToken).ConfigureAwait(false);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            _current = loaded ?? new AppSettings();
            _logger.LogInformation("Settings loaded from {Path}", _settingsPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings from {Path}; reverting to defaults", _settingsPath);
            _current = new AppSettings();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }

        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _current = new AppSettings();
            await SaveInternalAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Settings reset to defaults");
        }
        finally
        {
            _lock.Release();
        }

        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }

    // ── Private helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Inner save — caller MUST hold <see cref="_lock"/>.
    /// </summary>
    private async Task SaveInternalAsync(CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(_current, JsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Settings saved to {Path}", _settingsPath);
    }
}
