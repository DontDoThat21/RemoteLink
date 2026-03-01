namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Provides load/save/reset operations for persisted application settings.
/// Implementations must be thread-safe.
/// </summary>
public interface IAppSettingsService
{
    /// <summary>
    /// Gets the currently loaded settings. Never <c>null</c>; returns defaults
    /// if <see cref="LoadAsync"/> has not yet been called.
    /// </summary>
    Models.AppSettings Current { get; }

    /// <summary>
    /// Loads settings from persistent storage, populating <see cref="Current"/>.
    /// Creates a default settings file if none exists.
    /// </summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the current <see cref="Current"/> settings to storage.
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets <see cref="Current"/> to factory defaults and saves immediately.
    /// </summary>
    Task ResetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised whenever settings are saved (from any call-site).
    /// </summary>
    event EventHandler? SettingsSaved;
}
