using RemoteLink.Desktop.UI.Services;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.UI;

/// <summary>
/// Settings and preferences window — all seven categories: General, Security,
/// Network, Display, Audio, Recording, and Startup.
/// All UI is code-behind; no XAML.
/// </summary>
public class SettingsPage : ContentPage
{
    private readonly IAppSettingsService _settingsService;
    private readonly StartupTaskService _startupTaskService;

    // Track the active section for highlighting
    private readonly Dictionary<string, Button> _sectionButtons = new();
    private readonly Dictionary<string, View> _sectionPanels = new();
    private string _activeSection = "General";

    // ── General ──────────────────────────────────────────────────────────
    private Picker? _themePicker;
    private Switch? _minimizeToTraySw;
    private Switch? _showConnectionNotifSw;
    private Switch? _confirmDisconnectSw;

    // ── Security ─────────────────────────────────────────────────────────
    private Entry? _pinExpiryEntry;
    private Entry? _idleDisconnectEntry;
    private Entry? _maxAuthAttemptsEntry;
    private Switch? _allowUnattendedSw;
    private Switch? _requireTlsSw;
    private Switch? _lockOnSessionEndSw;

    // ── Network ──────────────────────────────────────────────────────────
    private Entry? _hostPortEntry;
    private Entry? _discoveryPortEntry;
    private Entry? _connTimeoutEntry;
    private Switch? _enableDiscoverySw;

    // ── Display ──────────────────────────────────────────────────────────
    private Entry? _targetFpsEntry;
    private Entry? _imageQualityEntry;
    private Picker? _imageFormatPicker;
    private Switch? _deltaEncodingSw;

    // ── Audio ────────────────────────────────────────────────────────────
    private Switch? _enableAudioSw;
    private Entry? _sampleRateEntry;
    private Entry? _channelsEntry;

    // ── Recording ────────────────────────────────────────────────────────
    private Switch? _enableRecordingSw;
    private Entry? _outputDirEntry;
    private Entry? _autoDeleteEntry;

    // ── Startup ──────────────────────────────────────────────────────────
    private Switch? _launchOnStartupSw;
    private Switch? _startHostAutoSw;
    private Switch? _startMinimizedSw;

    // ── Status feedback ──────────────────────────────────────────────────
    private Label? _feedbackLabel;

    public SettingsPage(IAppSettingsService settingsService, StartupTaskService startupTaskService)
    {
        _settingsService = settingsService;
        _startupTaskService = startupTaskService;
        Title = "Settings";
        BackgroundColor = ThemeColors.PageBackground;

        Content = BuildLayout();
        PopulateControls();
    }

    // ── Layout ───────────────────────────────────────────────────────────

    private View BuildLayout()
    {
        var sections = new[] { "General", "Security", "Network", "Display", "Audio", "Recording", "Startup" };

        // ── Left navigation sidebar ──
        var navStack = new StackLayout
        {
            BackgroundColor = ThemeColors.SettingsSidebarBackground,
            Padding = new Thickness(0, 8),
            Spacing = 2,
            WidthRequest = 150
        };

        navStack.Add(new Label
        {
            Text = "SETTINGS",
            FontSize = 10,
            TextColor = ThemeColors.TextSecondary,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(14, 8, 0, 6)
        });

        foreach (var section in sections)
        {
            var btn = CreateNavButton(section);
            _sectionButtons[section] = btn;
            navStack.Add(btn);
        }

        // ── Content area ──
        var panels = new Grid();
        foreach (var section in sections)
        {
            var panel = BuildSectionPanel(section);
            panel.IsVisible = section == _activeSection;
            _sectionPanels[section] = panel;
            panels.Add(panel);
        }

        var contentArea = new ScrollView
        {
            Content = panels,
            VerticalOptions = LayoutOptions.Fill
        };

        // ── Save / Reset / feedback bar ──
        _feedbackLabel = new Label
        {
            Text = "",
            FontSize = 12,
            TextColor = ThemeColors.Success,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false
        };

        var saveButton = new Button
        {
            Text = "Save",
            FontSize = 14,
            BackgroundColor = ThemeColors.Accent,
            TextColor = Colors.White,
            CornerRadius = 6,
            HeightRequest = 38,
            WidthRequest = 90,
            Margin = new Thickness(0, 0, 8, 0)
        };
        saveButton.Clicked += OnSaveClicked;

        var resetButton = new Button
        {
            Text = "Reset Defaults",
            FontSize = 13,
            BackgroundColor = ThemeColors.ResetButtonBackground,
            TextColor = ThemeColors.TextPrimary,
            CornerRadius = 6,
            HeightRequest = 38,
            WidthRequest = 120
        };
        resetButton.Clicked += OnResetClicked;

        var bottomBar = new Grid
        {
            BackgroundColor = ThemeColors.SettingsBottomBar,
            Padding = new Thickness(16, 8),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
            Children =
            {
                _feedbackLabel,
                CreateGridChild(saveButton, column: 1),
                CreateGridChild(resetButton, column: 2),
            }
        };

        // ── Root layout ──
        var body = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(150, GridUnitType.Absolute)),
                new ColumnDefinition(GridLength.Star),
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
            ColumnSpacing = 0,
            RowSpacing = 0
        };

        body.Add(navStack);
        body.Add(contentArea, 1, 0);
        Grid.SetRowSpan(navStack, 2);
        body.Add(bottomBar, 1, 1);

        // Apply initial selection highlight
        HighlightNavButton(_activeSection);

        return body;
    }

    private Button CreateNavButton(string section)
    {
        var btn = new Button
        {
            Text = section,
            FontSize = 13,
            BackgroundColor = Colors.Transparent,
            TextColor = ThemeColors.SettingsNavInactive,
            HorizontalOptions = LayoutOptions.Fill,
            CornerRadius = 0,
            Padding = new Thickness(16, 10),
            HeightRequest = 40
        };
        btn.Clicked += (_, _) => ShowSection(section);
        return btn;
    }

    private void ShowSection(string section)
    {
        if (_activeSection == section) return;

        if (_sectionPanels.TryGetValue(_activeSection, out var oldPanel))
            oldPanel.IsVisible = false;

        _activeSection = section;

        if (_sectionPanels.TryGetValue(section, out var newPanel))
            newPanel.IsVisible = true;

        HighlightNavButton(section);
    }

    private void HighlightNavButton(string section)
    {
        foreach (var (key, btn) in _sectionButtons)
        {
            btn.BackgroundColor = key == section
                ? ThemeColors.Accent
                : Colors.Transparent;
            btn.TextColor = key == section
                ? Colors.White
                : ThemeColors.SettingsNavInactive;
        }
    }

    // ── Section panels ────────────────────────────────────────────────────

    private View BuildSectionPanel(string section) => section switch
    {
        "General"   => BuildGeneralPanel(),
        "Security"  => BuildSecurityPanel(),
        "Network"   => BuildNetworkPanel(),
        "Display"   => BuildDisplayPanel(),
        "Audio"     => BuildAudioPanel(),
        "Recording" => BuildRecordingPanel(),
        "Startup"   => BuildStartupPanel(),
        _           => new Label { Text = section }
    };

    private View BuildGeneralPanel()
    {
        _themePicker = new Picker
        {
            Title = "Theme",
            BackgroundColor = ThemeColors.EntryBackground,
            HeightRequest = 36
        };
        _themePicker.Items.Add("System");
        _themePicker.Items.Add("Light");
        _themePicker.Items.Add("Dark");

        _minimizeToTraySw       = new Switch();
        _showConnectionNotifSw  = new Switch();
        _confirmDisconnectSw    = new Switch();

        return BuildSection("General", new[]
        {
            BuildPickerRow("Theme",
                "Follow the OS dark/light setting, or force a specific theme.",
                _themePicker),
            BuildSwitchRow("Minimise to tray on close",
                "Hide the main window instead of quitting when the close button is pressed.",
                _minimizeToTraySw),
            BuildSwitchRow("Show connection notifications",
                "Display a toast notification when a client connects or disconnects.",
                _showConnectionNotifSw),
            BuildSwitchRow("Confirm before disconnecting",
                "Show a confirmation prompt before ending an active session.",
                _confirmDisconnectSw),
        });
    }

    private View BuildSecurityPanel()
    {
        _pinExpiryEntry       = BuildNumericEntry("10", 5);
        _idleDisconnectEntry  = BuildNumericEntry("0", 5);
        _maxAuthAttemptsEntry = BuildNumericEntry("5", 3);
        _allowUnattendedSw    = new Switch();
        _requireTlsSw         = new Switch();
        _lockOnSessionEndSw   = new Switch();

        return BuildSection("Security", new[]
        {
            BuildEntryRow("PIN expiry (minutes)",
                "Minutes before the one-time PIN expires. Enter 0 to disable expiry.",
                _pinExpiryEntry),
            BuildEntryRow("Idle disconnect (minutes)",
                "Automatically disconnect an active remote session after this much client inactivity. Enter 0 to disable.",
                _idleDisconnectEntry),
            BuildEntryRow("Max auth attempts",
                "Consecutive failed PIN attempts allowed before the device is locked out.",
                _maxAuthAttemptsEntry),
            BuildSwitchRow("Allow unattended access",
                "Permit connections without interactive PIN confirmation. Use with caution.",
                _allowUnattendedSw),
            BuildSwitchRow("Require TLS encryption",
                "Reject connections that do not support TLS 1.2 or higher.",
                _requireTlsSw),
            BuildSwitchRow("Lock workstation on session end",
                "Lock the local Windows session when a remote client disconnects.",
                _lockOnSessionEndSw),
        });
    }

    private View BuildNetworkPanel()
    {
        _hostPortEntry       = BuildNumericEntry("12346", 6);
        _discoveryPortEntry  = BuildNumericEntry("12347", 6);
        _connTimeoutEntry    = BuildNumericEntry("30", 5);
        _enableDiscoverySw   = new Switch();

        return BuildSection("Network", new[]
        {
            BuildEntryRow("Host port",
                "TCP port the host listens on for incoming remote-desktop connections.",
                _hostPortEntry),
            BuildEntryRow("Discovery port",
                "UDP port used for LAN device discovery broadcasts.",
                _discoveryPortEntry),
            BuildEntryRow("Connection timeout (seconds)",
                "Seconds to wait for an outgoing connection attempt before giving up.",
                _connTimeoutEntry),
            BuildSwitchRow("Enable LAN discovery",
                "Broadcast device presence on the local network so nearby devices appear automatically.",
                _enableDiscoverySw),
        });
    }

    private View BuildDisplayPanel()
    {
        _targetFpsEntry    = BuildNumericEntry("30", 4);
        _imageQualityEntry = BuildNumericEntry("80", 4);
        _imageFormatPicker = new Picker
        {
            Title = "Format",
            BackgroundColor = ThemeColors.EntryBackground,
            HeightRequest = 36
        };
        _imageFormatPicker.Items.Add("JPEG");
        _imageFormatPicker.Items.Add("PNG");

        _deltaEncodingSw = new Switch();

        return BuildSection("Display", new[]
        {
            BuildEntryRow("Target FPS",
                "Maximum frames per second streamed to connected clients (1–60).",
                _targetFpsEntry),
            BuildEntryRow("Image quality (1–100)",
                "JPEG compression quality. Higher values mean better quality and more bandwidth.",
                _imageQualityEntry),
            BuildPickerRow("Image format",
                "Encoding used for each captured frame. JPEG uses less bandwidth; PNG is lossless.",
                _imageFormatPicker),
            BuildSwitchRow("Delta (changed-region) encoding",
                "Send only the pixels that changed since the last frame to reduce bandwidth.",
                _deltaEncodingSw),
        });
    }

    private View BuildAudioPanel()
    {
        _enableAudioSw  = new Switch();
        _sampleRateEntry = BuildNumericEntry("44100", 7);
        _channelsEntry   = BuildNumericEntry("2", 2);

        return BuildSection("Audio", new[]
        {
            BuildSwitchRow("Enable audio streaming",
                "Capture and forward desktop audio to connected clients.",
                _enableAudioSw),
            BuildEntryRow("Sample rate (Hz)",
                "Audio sample rate in Hz. Common values: 44100 (CD), 48000 (broadcast).",
                _sampleRateEntry),
            BuildEntryRow("Channels",
                "1 = mono, 2 = stereo.",
                _channelsEntry),
        });
    }

    private View BuildRecordingPanel()
    {
        _enableRecordingSw = new Switch();
        _outputDirEntry    = new Entry
        {
            BackgroundColor = ThemeColors.EntryBackground,
            HeightRequest   = 36,
            FontSize        = 13,
            TextColor       = ThemeColors.TextPrimary
        };
        _autoDeleteEntry = BuildNumericEntry("0", 4);

        return BuildSection("Recording", new[]
        {
            BuildSwitchRow("Auto-record sessions",
                "Automatically record every remote session to disk using FFmpeg.",
                _enableRecordingSw),
            BuildEntryRow("Output directory",
                "Folder where session recordings are saved.",
                _outputDirEntry),
            BuildEntryRow("Auto-delete after (days)",
                "Delete recordings older than this many days. Enter 0 to keep forever.",
                _autoDeleteEntry),
        });
    }

    private View BuildStartupPanel()
    {
        _launchOnStartupSw = new Switch();
        _startHostAutoSw   = new Switch();
        _startMinimizedSw  = new Switch();

        return BuildSection("Startup", new[]
        {
            BuildSwitchRow("Launch RemoteLink on Windows startup",
                "Register RemoteLink in the Windows registry to start with the OS.",
                _launchOnStartupSw),
            BuildSwitchRow("Auto-start host service",
                "Automatically start listening for remote connections when the app launches.",
                _startHostAutoSw),
            BuildSwitchRow("Start minimised",
                "Start RemoteLink minimised to the system tray instead of showing the main window.",
                _startMinimizedSw),
        });
    }

    // ── Row builders ──────────────────────────────────────────────────────

    private static View BuildSection(string title, View[] rows)
    {
        var stack = new StackLayout
        {
            Padding = new Thickness(24, 20),
            Spacing = 0
        };

        stack.Add(new Label
        {
            Text = title,
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.TextPrimary,
            Margin = new Thickness(0, 0, 0, 16)
        });

        var separator = new BoxView
        {
            Color = ThemeColors.SectionSeparator,
            HeightRequest = 1,
            HorizontalOptions = LayoutOptions.Fill,
            Margin = new Thickness(0, 0, 0, 20)
        };
        stack.Add(separator);

        foreach (var row in rows)
            stack.Add(row);

        return stack;
    }

    private static View BuildSwitchRow(string label, string description, Switch sw)
    {
        var labelView = new Label
        {
            Text = label,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.TextPrimary,
            VerticalOptions = LayoutOptions.Center
        };

        var descView = new Label
        {
            Text = description,
            FontSize = 11,
            TextColor = ThemeColors.TextSecondary,
            Margin = new Thickness(0, 2, 0, 0)
        };

        var textStack = new StackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            Children = { labelView, descView }
        };

        sw.VerticalOptions = LayoutOptions.Center;

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            Padding = new Thickness(0, 8),
            Children =
            {
                textStack,
                CreateGridChild(sw, column: 1)
            }
        };

        var border = new BoxView
        {
            Color = ThemeColors.SeparatorLight,
            HeightRequest = 1,
            HorizontalOptions = LayoutOptions.Fill
        };

        var wrapper = new StackLayout
        {
            Spacing = 0,
            Children = { row, border }
        };

        return wrapper;
    }

    private static View BuildEntryRow(string label, string description, Entry entry)
    {
        var labelView = new Label
        {
            Text = label,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.TextPrimary,
            VerticalOptions = LayoutOptions.Center
        };

        var descView = new Label
        {
            Text = description,
            FontSize = 11,
            TextColor = ThemeColors.TextSecondary,
            Margin = new Thickness(0, 2, 0, 0)
        };

        var textStack = new StackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            Children = { labelView, descView }
        };

        entry.VerticalOptions = LayoutOptions.Center;
        entry.HorizontalOptions = LayoutOptions.End;
        entry.WidthRequest = 120;

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            Padding = new Thickness(0, 8),
            Children =
            {
                textStack,
                CreateGridChild(entry, column: 1)
            }
        };

        var border = new BoxView
        {
            Color = ThemeColors.SeparatorLight,
            HeightRequest = 1,
            HorizontalOptions = LayoutOptions.Fill
        };

        return new StackLayout { Spacing = 0, Children = { row, border } };
    }

    private static View BuildPickerRow(string label, string description, Picker picker)
    {
        var labelView = new Label
        {
            Text = label,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.TextPrimary,
            VerticalOptions = LayoutOptions.Center
        };

        var descView = new Label
        {
            Text = description,
            FontSize = 11,
            TextColor = ThemeColors.TextSecondary,
            Margin = new Thickness(0, 2, 0, 0)
        };

        var textStack = new StackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            Children = { labelView, descView }
        };

        picker.VerticalOptions = LayoutOptions.Center;
        picker.HorizontalOptions = LayoutOptions.End;
        picker.WidthRequest = 120;

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            Padding = new Thickness(0, 8),
            Children =
            {
                textStack,
                CreateGridChild(picker, column: 1)
            }
        };

        var border = new BoxView
        {
            Color = ThemeColors.SeparatorLight,
            HeightRequest = 1,
            HorizontalOptions = LayoutOptions.Fill
        };

        return new StackLayout { Spacing = 0, Children = { row, border } };
    }

    private static Entry BuildNumericEntry(string defaultValue, int maxLength) =>
        new()
        {
            Text = defaultValue,
            MaxLength = maxLength,
            Keyboard = Keyboard.Numeric,
            BackgroundColor = ThemeColors.EntryBackground,
            HeightRequest = 36,
            FontSize = 13,
            TextColor = ThemeColors.TextPrimary
        };

    // ── Populate from model ───────────────────────────────────────────────

    private void PopulateControls()
    {
        var s = _settingsService.Current;

        // General
        if (_themePicker           != null) _themePicker.SelectedIndex       = (int)s.General.Theme;
        if (_minimizeToTraySw      != null) _minimizeToTraySw.IsToggled      = s.General.MinimizeToTray;
        if (_showConnectionNotifSw != null) _showConnectionNotifSw.IsToggled = s.General.ShowConnectionNotifications;
        if (_confirmDisconnectSw   != null) _confirmDisconnectSw.IsToggled   = s.General.ConfirmDisconnect;

        // Security
        if (_pinExpiryEntry       != null) _pinExpiryEntry.Text          = s.Security.PinExpiryMinutes.ToString();
        if (_maxAuthAttemptsEntry != null) _maxAuthAttemptsEntry.Text     = s.Security.MaxAuthAttempts.ToString();
        if (_allowUnattendedSw    != null) _allowUnattendedSw.IsToggled  = s.Security.AllowUnattendedAccess;
        if (_requireTlsSw         != null) _requireTlsSw.IsToggled       = s.Security.RequireTls;
        if (_lockOnSessionEndSw   != null) _lockOnSessionEndSw.IsToggled = s.Security.LockOnSessionEnd;

        // Network
        if (_hostPortEntry      != null) _hostPortEntry.Text       = s.Network.HostPort.ToString();
        if (_discoveryPortEntry != null) _discoveryPortEntry.Text  = s.Network.DiscoveryPort.ToString();
        if (_connTimeoutEntry   != null) _connTimeoutEntry.Text    = s.Network.ConnectionTimeoutSeconds.ToString();
        if (_enableDiscoverySw  != null) _enableDiscoverySw.IsToggled = s.Network.EnableDiscovery;

        // Display
        if (_targetFpsEntry    != null) _targetFpsEntry.Text       = s.Display.TargetFps.ToString();
        if (_imageQualityEntry != null) _imageQualityEntry.Text    = s.Display.ImageQuality.ToString();
        if (_imageFormatPicker != null) _imageFormatPicker.SelectedIndex = s.Display.ImageFormat == RemoteLink.Shared.Models.ImageFormat.Png ? 1 : 0;
        if (_deltaEncodingSw   != null) _deltaEncodingSw.IsToggled = s.Display.UseDeltaEncoding;

        // Audio
        if (_enableAudioSw   != null) _enableAudioSw.IsToggled = s.Audio.EnableAudio;
        if (_sampleRateEntry != null) _sampleRateEntry.Text     = s.Audio.SampleRate.ToString();
        if (_channelsEntry   != null) _channelsEntry.Text       = s.Audio.Channels.ToString();

        // Recording
        if (_enableRecordingSw != null) _enableRecordingSw.IsToggled = s.Recording.EnableRecording;
        if (_outputDirEntry    != null) _outputDirEntry.Text          = s.Recording.OutputDirectory;
        if (_autoDeleteEntry   != null) _autoDeleteEntry.Text         = s.Recording.AutoDeleteAfterDays.ToString();

        // Startup
        if (_launchOnStartupSw != null) _launchOnStartupSw.IsToggled = s.Startup.LaunchOnWindowsStartup;
        if (_startHostAutoSw   != null) _startHostAutoSw.IsToggled   = s.Startup.StartHostAutomatically;
        if (_startMinimizedSw  != null) _startMinimizedSw.IsToggled  = s.Startup.StartMinimized;
    }

    // ── Collect controls into model ───────────────────────────────────────

    private void CollectControls()
    {
        var s = _settingsService.Current;

        // General
        if (_themePicker != null && _themePicker.SelectedIndex >= 0)
            s.General.Theme = (ThemeMode)_themePicker.SelectedIndex;
        s.General.MinimizeToTray                = _minimizeToTraySw?.IsToggled      ?? s.General.MinimizeToTray;
        s.General.ShowConnectionNotifications   = _showConnectionNotifSw?.IsToggled ?? s.General.ShowConnectionNotifications;
        s.General.ConfirmDisconnect             = _confirmDisconnectSw?.IsToggled   ?? s.General.ConfirmDisconnect;

        // Security
        if (int.TryParse(_pinExpiryEntry?.Text, out int pinExpiry) && pinExpiry >= 0)
            s.Security.PinExpiryMinutes = pinExpiry;
        if (int.TryParse(_idleDisconnectEntry?.Text, out int idleDisconnect) && idleDisconnect >= 0)
            s.Security.IdleDisconnectMinutes = idleDisconnect;
        if (int.TryParse(_maxAuthAttemptsEntry?.Text, out int maxAttempts) && maxAttempts > 0)
            s.Security.MaxAuthAttempts = maxAttempts;
        s.Security.AllowUnattendedAccess = _allowUnattendedSw?.IsToggled  ?? s.Security.AllowUnattendedAccess;
        s.Security.RequireTls            = _requireTlsSw?.IsToggled       ?? s.Security.RequireTls;
        s.Security.LockOnSessionEnd      = _lockOnSessionEndSw?.IsToggled ?? s.Security.LockOnSessionEnd;

        // Network
        if (int.TryParse(_hostPortEntry?.Text, out int hostPort) && hostPort is > 0 and <= 65535)
            s.Network.HostPort = hostPort;
        if (int.TryParse(_discoveryPortEntry?.Text, out int discoveryPort) && discoveryPort is > 0 and <= 65535)
            s.Network.DiscoveryPort = discoveryPort;
        if (int.TryParse(_connTimeoutEntry?.Text, out int timeout) && timeout > 0)
            s.Network.ConnectionTimeoutSeconds = timeout;
        s.Network.EnableDiscovery = _enableDiscoverySw?.IsToggled ?? s.Network.EnableDiscovery;

        // Display
        if (int.TryParse(_targetFpsEntry?.Text, out int fps) && fps is >= 1 and <= 60)
            s.Display.TargetFps = fps;
        if (int.TryParse(_imageQualityEntry?.Text, out int quality) && quality is >= 1 and <= 100)
            s.Display.ImageQuality = quality;
        if (_imageFormatPicker != null)
            s.Display.ImageFormat = _imageFormatPicker.SelectedIndex == 1 ? RemoteLink.Shared.Models.ImageFormat.Png : RemoteLink.Shared.Models.ImageFormat.Jpeg;
        s.Display.UseDeltaEncoding = _deltaEncodingSw?.IsToggled ?? s.Display.UseDeltaEncoding;

        // Audio
        s.Audio.EnableAudio = _enableAudioSw?.IsToggled ?? s.Audio.EnableAudio;
        if (int.TryParse(_sampleRateEntry?.Text, out int sampleRate) && sampleRate > 0)
            s.Audio.SampleRate = sampleRate;
        if (int.TryParse(_channelsEntry?.Text, out int channels) && channels is 1 or 2)
            s.Audio.Channels = channels;

        // Recording
        s.Recording.EnableRecording = _enableRecordingSw?.IsToggled ?? s.Recording.EnableRecording;
        if (!string.IsNullOrWhiteSpace(_outputDirEntry?.Text))
            s.Recording.OutputDirectory = _outputDirEntry.Text.Trim();
        if (int.TryParse(_autoDeleteEntry?.Text, out int autoDelete) && autoDelete >= 0)
            s.Recording.AutoDeleteAfterDays = autoDelete;

        // Startup
        s.Startup.LaunchOnWindowsStartup = _launchOnStartupSw?.IsToggled ?? s.Startup.LaunchOnWindowsStartup;
        s.Startup.StartHostAutomatically  = _startHostAutoSw?.IsToggled   ?? s.Startup.StartHostAutomatically;
        s.Startup.StartMinimized          = _startMinimizedSw?.IsToggled  ?? s.Startup.StartMinimized;
    }

    // ── Event handlers ────────────────────────────────────────────────────

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        CollectControls();

        try
        {
            await _settingsService.SaveAsync();

            // Apply auto-start with Windows setting
            var launchOnStartup = _settingsService.Current.Startup.LaunchOnWindowsStartup;
            var success = await _startupTaskService.SetEnabledAsync(launchOnStartup);
            if (!success && launchOnStartup)
            {
                ShowFeedback("Settings saved, but auto-start registration failed.", ThemeColors.Warning);
                return;
            }

            ShowFeedback("Settings saved.", ThemeColors.Success);
        }
        catch (Exception ex)
        {
            ShowFeedback($"Save failed: {ex.Message}", ThemeColors.Danger);
        }
    }

    private async void OnResetClicked(object? sender, EventArgs e)
    {
        bool confirm = await DisplayAlertAsync(
            "Reset Settings",
            "Reset all settings to their factory defaults? This cannot be undone.",
            "Reset",
            "Cancel");

        if (!confirm) return;

        try
        {
            await _settingsService.ResetAsync();
            PopulateControls();
            ShowFeedback("Settings reset to defaults.", ThemeColors.Success);
        }
        catch (Exception ex)
        {
            ShowFeedback($"Reset failed: {ex.Message}", ThemeColors.Danger);
        }
    }

    private void ShowFeedback(string message, Color color)
    {
        if (_feedbackLabel == null) return;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            _feedbackLabel.Text = message;
            _feedbackLabel.TextColor = color;
            _feedbackLabel.IsVisible = true;
            await Task.Delay(3000);
            _feedbackLabel.IsVisible = false;
        });
    }

    // ── Shared helpers ────────────────────────────────────────────────────

    private static View CreateGridChild(View view, int column = 0, int row = 0)
    {
        Grid.SetColumn(view, column);
        Grid.SetRow(view, row);
        return view;
    }
}
