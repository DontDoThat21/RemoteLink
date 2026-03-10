namespace RemoteLink.Shared.Models;

/// <summary>
/// Persisted application settings for RemoteLink applications.
/// All sections are represented as nested classes for logical grouping.
/// </summary>
public class AppSettings
{
    /// <summary>General application behaviour.</summary>
    public GeneralSettings General { get; set; } = new();

    /// <summary>Authentication and access-control preferences.</summary>
    public SecuritySettings Security { get; set; } = new();

    /// <summary>Network and connectivity preferences.</summary>
    public NetworkSettings Network { get; set; } = new();

    /// <summary>Screen-capture and image-quality preferences.</summary>
    public DisplaySettings Display { get; set; } = new();

    /// <summary>Client-side input preferences.</summary>
    public InputSettings Input { get; set; } = new();

    /// <summary>Audio capture preferences.</summary>
    public AudioSettings Audio { get; set; } = new();

    /// <summary>Session recording preferences.</summary>
    public RecordingSettings Recording { get; set; } = new();

    /// <summary>Windows startup preferences.</summary>
    public StartupSettings Startup { get; set; } = new();

    // ── Nested setting classes ───────────────────────────────────────────

    /// <summary>General application behaviour settings.</summary>
    public class GeneralSettings
    {
        /// <summary>Minimise to system tray when the main window is closed.</summary>
        public bool MinimizeToTray { get; set; } = true;

        /// <summary>Show a notification when a client connects or disconnects.</summary>
        public bool ShowConnectionNotifications { get; set; } = true;

        /// <summary>Confirm before disconnecting an active session.</summary>
        public bool ConfirmDisconnect { get; set; } = true;

        /// <summary>UI theme mode: System (follow OS), Light, or Dark.</summary>
        public ThemeMode Theme { get; set; } = ThemeMode.System;
    }

    /// <summary>Authentication and access-control settings.</summary>
    public class SecuritySettings
    {
        /// <summary>Minutes before the one-time PIN expires (0 = never).</summary>
        public int PinExpiryMinutes { get; set; } = 10;

        /// <summary>Maximum consecutive failed PIN attempts before lockout.</summary>
        public int MaxAuthAttempts { get; set; } = 5;

        /// <summary>Allow connections without interactive PIN confirmation (unattended access).</summary>
        public bool AllowUnattendedAccess { get; set; } = false;

        /// <summary>Require TLS for all incoming connections.</summary>
        public bool RequireTls { get; set; } = true;

        /// <summary>Lock the local workstation when a remote session ends.</summary>
        public bool LockOnSessionEnd { get; set; } = false;

        /// <summary>Require the mobile client to unlock with a local PIN after inactivity/backgrounding.</summary>
        public bool EnableAppLock { get; set; } = false;

        /// <summary>Minutes the mobile app may stay in the background before locking again (0 = immediately).</summary>
        public int AppLockTimeoutMinutes { get; set; } = 1;
    }

    /// <summary>Network and connectivity settings.</summary>
    public class NetworkSettings
    {
        /// <summary>TCP port the host listens on for incoming remote-desktop connections.</summary>
        public int HostPort { get; set; } = 12346;

        /// <summary>UDP port used for LAN device discovery broadcasts.</summary>
        public int DiscoveryPort { get; set; } = 12347;

        /// <summary>Seconds before an idle connection attempt is abandoned.</summary>
        public int ConnectionTimeoutSeconds { get; set; } = 30;

        /// <summary>Enable UDP LAN discovery broadcasts.</summary>
        public bool EnableDiscovery { get; set; } = true;
    }

    /// <summary>Screen-capture and image quality settings.</summary>
    public class DisplaySettings
    {
        /// <summary>Allow the host to adapt image quality automatically based on network conditions.</summary>
        public bool EnableAdaptiveQuality { get; set; } = true;

        /// <summary>Maximum frames per second sent to connected clients.</summary>
        public int TargetFps { get; set; } = 30;

        /// <summary>JPEG compression quality (1–100; higher = better quality, more bandwidth).</summary>
        public int ImageQuality { get; set; } = 80;

        /// <summary>Image encoding format sent over the wire.</summary>
        public ImageFormat ImageFormat { get; set; } = ImageFormat.Jpeg;

        /// <summary>Send only changed regions between frames (delta encoding).</summary>
        public bool UseDeltaEncoding { get; set; } = true;
    }

    /// <summary>Client-side touch and gesture settings.</summary>
    public class InputSettings
    {
        /// <summary>Multiplier applied to touch gesture movement and scroll translation.</summary>
        public double GestureSensitivity { get; set; } = 1.0;
    }

    /// <summary>Audio capture settings.</summary>
    public class AudioSettings
    {
        /// <summary>Capture and stream desktop audio to connected clients.</summary>
        public bool EnableAudio { get; set; } = false;

        /// <summary>Audio sample rate in Hz.</summary>
        public int SampleRate { get; set; } = 44100;

        /// <summary>Number of audio channels (1 = mono, 2 = stereo).</summary>
        public int Channels { get; set; } = 2;
    }

    /// <summary>Session recording settings.</summary>
    public class RecordingSettings
    {
        /// <summary>Automatically record every remote session to disk.</summary>
        public bool EnableRecording { get; set; } = false;

        /// <summary>Directory where session recordings are saved.</summary>
        public string OutputDirectory { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RemoteLink", "Recordings");

        /// <summary>Automatically delete recordings older than this many days (0 = keep forever).</summary>
        public int AutoDeleteAfterDays { get; set; } = 0;
    }

    /// <summary>Windows startup settings.</summary>
    public class StartupSettings
    {
        /// <summary>Launch RemoteLink when Windows starts.</summary>
        public bool LaunchOnWindowsStartup { get; set; } = false;

        /// <summary>Automatically start the host service when the application launches.</summary>
        public bool StartHostAutomatically { get; set; } = false;

        /// <summary>Start minimised to the system tray.</summary>
        public bool StartMinimized { get; set; } = false;
    }
}

/// <summary>UI theme mode.</summary>
public enum ThemeMode
{
    /// <summary>Follow the OS dark/light setting.</summary>
    System,

    /// <summary>Always use the light theme.</summary>
    Light,

    /// <summary>Always use the dark theme.</summary>
    Dark
}

/// <summary>Image encoding format used for screen-capture frames.</summary>
public enum ImageFormat
{
    /// <summary>JPEG — lossy, low bandwidth, suitable for most connections.</summary>
    Jpeg,

    /// <summary>PNG — lossless, higher quality, higher bandwidth.</summary>
    Png
}
