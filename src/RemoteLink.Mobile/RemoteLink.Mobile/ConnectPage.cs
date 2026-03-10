using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Web;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using ZXing.Net.Maui;
using DeviceInfo = RemoteLink.Shared.Models.DeviceInfo;
using DeviceType = RemoteLink.Shared.Models.DeviceType;

namespace RemoteLink.Mobile;

/// <summary>
/// Connect tab: manual Partner ID + PIN entry, discovered host quick-connect,
/// remote desktop viewer with gesture-based input when connected.
/// </summary>
public class ConnectPage : ContentPage, INotifyPropertyChanged
{
    private readonly ILogger<ConnectPage> _logger;
    private readonly RemoteDesktopClient _client;
    private readonly INetworkDiscovery _networkDiscovery;
    private readonly IConnectionHistoryService _connectionHistory;
    private readonly IAppSettingsService _settingsService;
    private string _manualStatusText = string.Empty;
    private Color _manualStatusColor = Colors.Transparent;
    private bool _manualStatusVisible;
    private string _monitorStatusText = "Loading remote monitors...";
    private Color _monitorStatusColor = Colors.Gray;

    // Touch-to-mouse translation
    private readonly TouchToMouseTranslator _touchTranslator = new();

    // Target desktop resolution
    private int _desktopWidth = 1920;
    private int _desktopHeight = 1080;

    // State
    private bool _isDiscovering;
    private string _statusMessage = "Initializing...";
    private readonly List<DeviceInfo> _availableHosts = new();
    private bool _isManualConnecting;
    private bool _showConnectionNotifications = true;
    private bool _adaptiveQualityEnabled = true;
    private float _gestureSensitivity = 1.0f;
    private ScreenDataFormat _preferredImageFormat = ScreenDataFormat.JPEG;
    private bool _audioStreamingEnabled;
    private bool _hasConnectedSession;

    // Throttle frame rendering
    private volatile bool _frameRenderBusy;

    // Track connection start time for duration calculation
    private DateTime? _connectionStartedAt;

    // UI references — manual connect
    private Entry _partnerIdEntry = null!;
    private Entry _pinEntry = null!;
    private Button _manualConnectButton = null!;
    private Label _manualStatusLabel = null!;

    // UI references — discovered hosts
    private StackLayout _hostListContainer = null!;
    private Label _noHostsLabel = null!;

    // UI references — connection / viewer
    private StackLayout _connectedBanner = null!;
    private Label _connectedHostLabel = null!;
    private Image _remoteViewer = null!;
    private Label _statusLabel = null!;
    private ActivityIndicator _activityIndicator = null!;
    private Border _qualityOverlay = null!;
    private Label _qualityTitleLabel = null!;
    private Label _qualityDetailsLabel = null!;
    private Border _sessionToolbar = null!;
    private Border _specialKeysBar = null!;
    private Entry _keyCaptureEntry = null!;
    private Button _keyboardToggleButton = null!;
    private Button _specialKeysToggleButton = null!;
    private Button _monitorButton = null!;
    private Border _monitorSelectorPanel = null!;
    private Label _monitorSelectorStatusLabel = null!;
    private HorizontalStackLayout _monitorCarousel = null!;

    // UI references — manual connect card (to hide when connected)
    private Border _manualConnectCard = null!;
    private View _scanQrButton = null!;
    private View _discoveredSection = null!;

    // Session toolbar state
    private bool _isKeyboardVisible;
    private bool _isSpecialKeysVisible;
    private bool _suppressKeyCaptureTextChanged;
    private int _selectedQuality = 75;
    private string? _selectedMonitorId;
    private bool _isMonitorSelectorVisible;
    private bool _isMonitorSelectorLoading;
    private readonly List<MonitorInfo> _remoteMonitors = new();
    private readonly Dictionary<string, Button> _modifierButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _modifierKeyCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = "Control",
        ["Alt"] = "Menu",
        ["Shift"] = "Shift",
        ["Win"] = "LWin"
    };
    private readonly HashSet<string> _activeModifiers = new(StringComparer.OrdinalIgnoreCase);

    // Bindable properties
    public bool IsDiscovering
    {
        get => _isDiscovering;
        set { _isDiscovering = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public ConnectPage(
        ILogger<ConnectPage> logger,
        RemoteDesktopClient client,
        INetworkDiscovery networkDiscovery,
        IConnectionHistoryService connectionHistory,
        IAppSettingsService settingsService)
    {
        _logger = logger;
        _client = client;
        _networkDiscovery = networkDiscovery;
        _connectionHistory = connectionHistory;
        _settingsService = settingsService;

        Title = "Connect";
        RefreshTheme();

        // Subscribe to client events
        _client.DeviceDiscovered += OnDeviceDiscovered;
        _client.DeviceLost += OnDeviceLost;
        _client.ServiceStatusChanged += OnServiceStatusChanged;
        _client.ConnectionStateChanged += OnConnectionStateChanged;
        _client.PairingFailed += OnPairingFailed;
        _client.ConnectionQualityUpdated += OnConnectionQualityUpdated;
        _client.ScreenDataReceived += OnScreenDataReceived;
        _settingsService.SettingsSaved += OnSettingsSaved;

        ApplyConnectionQuality(_client.CurrentConnectionQuality);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        ThemeColors.ThemeChanged += OnThemeChanged;

        RefreshTheme();

        await LoadSettingsAsync();

        // Reset manual connect UI when returning from a disconnected state
        if (!_client.IsConnected && _isManualConnecting)
        {
            _isManualConnecting = false;
            SetManualConnectButtonState("Connect", ThemeColors.Accent, true);
            _manualStatusLabel.Text = "";
            _manualStatusLabel.IsVisible = false;
        }

        if (!_client.IsStarted)
        {
            try
            {
                StatusMessage = "Starting discovery...";
                IsDiscovering = true;
                await _client.StartAsync();
                StatusMessage = "Scanning for desktop hosts...";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                IsDiscovering = false;
                _logger.LogError(ex, "Failed to start discovery");
            }
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ThemeColors.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        MainThread.BeginInvokeOnMainThread(RefreshTheme);
    }

    private void RefreshTheme()
    {
        var partnerId = _partnerIdEntry?.Text;
        var pin = _pinEntry?.Text;

        BackgroundColor = ThemeColors.PageBackground;
        Content = BuildLayout();

        if (_partnerIdEntry != null)
            _partnerIdEntry.Text = partnerId;

        if (_pinEntry != null)
            _pinEntry.Text = pin;

        _hostListContainer.Children.Clear();
        if (_availableHosts.Count == 0)
        {
            _hostListContainer.Add(_noHostsLabel);
        }
        else
        {
            foreach (var host in _availableHosts)
                AddHostCard(host);
        }

        _connectedBanner.IsVisible = _client.IsConnected;
        _remoteViewer.IsVisible = _client.IsConnected;
        _manualConnectCard.IsVisible = !_client.IsConnected;
        _scanQrButton.IsVisible = !_client.IsConnected;
        _discoveredSection.IsVisible = !_client.IsConnected;

        if (_client.IsConnected)
            _connectedHostLabel.Text = $"Connected to {_client.ConnectedHost?.DeviceName ?? "Unknown"}";

        if (_manualStatusVisible)
        {
            _manualStatusLabel.Text = _manualStatusText;
            _manualStatusLabel.TextColor = _manualStatusColor;
            _manualStatusLabel.IsVisible = true;
        }

        if (_isManualConnecting)
        {
            _manualConnectButton.Text = "Connecting...";
            _manualConnectButton.BackgroundColor = ThemeColors.NeutralButtonBackground;
            _manualConnectButton.IsEnabled = false;
        }
        else
        {
            _manualConnectButton.Text = "Connect";
            _manualConnectButton.BackgroundColor = ThemeColors.Accent;
            OnManualEntryChanged(null, null!);
        }

        SetMonitorStatus(_monitorStatusText, _monitorStatusColor);
        RebuildMonitorCarousel();
        ApplyConnectionQuality(_client.CurrentConnectionQuality);
        UpdateSessionOverlayVisibility();
        UpdateSessionActionButtonStates();
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            await _settingsService.LoadAsync();
            ApplySettings(_settingsService.Current);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load mobile settings");
        }
    }

    private void OnSettingsSaved(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ApplySettings(_settingsService.Current);

            if (_client.IsConnected)
                _ = ApplyPreferredSessionSettingsAsync(_client.ConnectedHost?.DeviceName ?? "host");
        });
    }

    private void ApplySettings(AppSettings settings)
    {
        _showConnectionNotifications = settings.General.ShowConnectionNotifications;
        _adaptiveQualityEnabled = settings.Display.EnableAdaptiveQuality;
        _selectedQuality = Math.Clamp(settings.Display.ImageQuality, 50, 85);
        _gestureSensitivity = Math.Clamp((float)settings.Input.GestureSensitivity, 0.5f, 2.0f);
        _preferredImageFormat = settings.Display.ImageFormat == RemoteLink.Shared.Models.ImageFormat.Png
            ? ScreenDataFormat.PNG
            : ScreenDataFormat.JPEG;
        _audioStreamingEnabled = settings.Audio.EnableAudio;
    }

    // ── Layout ─────────────────────────────────────────────────────────

    private View BuildLayout()
    {
        var root = new StackLayout
        {
            Padding = new Thickness(16, 16, 16, 120),
            Spacing = 12
        };

        // Header
        root.Add(new Label
        {
            Text = "RemoteLink",
            FontSize = 26,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            TextColor = ThemeColors.Accent,
            Margin = new Thickness(0, 8, 0, 0)
        });

        // Status row
        var statusRow = new StackLayout { Orientation = StackOrientation.Horizontal, Spacing = 8 };
        _activityIndicator = new ActivityIndicator
        {
            VerticalOptions = LayoutOptions.Center,
            Color = ThemeColors.Accent,
            WidthRequest = 20,
            HeightRequest = 20
        };
        _activityIndicator.SetBinding(ActivityIndicator.IsRunningProperty,
            new Binding(nameof(IsDiscovering), source: this));

        _statusLabel = new Label
        {
            FontSize = 14,
            VerticalOptions = LayoutOptions.Center,
            TextColor = ThemeColors.TextSecondary
        };
        _statusLabel.SetBinding(Label.TextProperty,
            new Binding(nameof(StatusMessage), source: this));

        statusRow.Add(_activityIndicator);
        statusRow.Add(_statusLabel);
        root.Add(statusRow);

        // Connected banner (hidden until connected)
        _connectedBanner = new StackLayout
        {
            BackgroundColor = ThemeColors.SuccessBackground,
            Padding = new Thickness(12),
            Spacing = 6,
            IsVisible = false
        };

        _connectedHostLabel = new Label
        {
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.SuccessText
        };

        var disconnectButton = new Button
        {
            Text = "Disconnect",
            BackgroundColor = ThemeColors.Danger,
            TextColor = Colors.White,
            FontSize = 14,
            CornerRadius = 6,
            HeightRequest = 36,
            HorizontalOptions = LayoutOptions.Start
        };
        disconnectButton.Clicked += async (_, _) => await DisconnectAsync();

        _connectedBanner.Add(_connectedHostLabel);
        _connectedBanner.Add(disconnectButton);
        root.Add(_connectedBanner);

        // Remote viewer surface
        _remoteViewer = new Image
        {
            BackgroundColor = Colors.Black,
            HeightRequest = 280,
            HorizontalOptions = LayoutOptions.Fill,
            Aspect = Aspect.AspectFit,
            IsVisible = false
        };
        AttachGestureRecognizers(_remoteViewer);

        root.Add(new Label
        {
            Text = "Remote Desktop",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.TextPrimary
        });
        root.Add(_remoteViewer);

        // ── Manual connection panel ────────────────────────────────────
        _manualConnectCard = BuildManualConnectCard();
        root.Add(_manualConnectCard);

        // ── Scan QR Code button ──────────────────────────────────────
        _scanQrButton = BuildScanQrButton();
        root.Add(_scanQrButton);

        // ── Discovered hosts (quick connect) ───────────────────────────
        _discoveredSection = BuildDiscoveredHostsSection();
        root.Add(_discoveredSection);

        _sessionToolbar = BuildSessionToolbar();
        _qualityOverlay = BuildQualityOverlay();
        _specialKeysBar = BuildSpecialKeysBar();
        _monitorSelectorPanel = BuildMonitorSelectorPanel();
        _keyCaptureEntry = BuildKeyCaptureEntry();
        UpdateSessionOverlayVisibility();

        var overlay = new VerticalStackLayout
        {
            Spacing = 8,
            Padding = new Thickness(16, 0, 16, 16),
            VerticalOptions = LayoutOptions.End,
            HorizontalOptions = LayoutOptions.Fill,
            Children = { _qualityOverlay, _specialKeysBar, _monitorSelectorPanel, _sessionToolbar, _keyCaptureEntry }
        };

        var layout = new Grid();
        layout.Add(new ScrollView { Content = root });
        layout.Add(overlay);
        return layout;
    }

    private Border BuildSessionToolbar()
    {
        _keyboardToggleButton = BuildSessionActionButton("⌨ Keyboard", OnKeyboardToggleClicked);
        _specialKeysToggleButton = BuildSessionActionButton("⌥ Keys", OnSpecialKeysToggleClicked);

        var qualityButton = BuildSessionActionButton("📊 Quality", OnQualityClicked);
        _monitorButton = BuildSessionActionButton("🖥 Monitor", OnMonitorClicked);
        var rebootButton = BuildSessionActionButton("↻ Reboot", OnRemoteRebootClicked, ThemeColors.WarningText);
        var disconnectButton = BuildSessionActionButton("✕ Disconnect", OnToolbarDisconnectClicked, ThemeColors.Danger);
        disconnectButton.TextColor = Colors.White;

        return new Border
        {
            IsVisible = false,
            BackgroundColor = ThemeColors.ToolbarBackground,
            Stroke = ThemeColors.ToolbarBorder,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 },
            Padding = new Thickness(12, 10),
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(ThemeColors.ShadowColor),
                Opacity = 0.12f,
                Radius = 16,
                Offset = new Point(0, 6)
            },
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new Label
                    {
                        Text = "Session Actions",
                        FontSize = 11,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = ThemeColors.AccentText
                    },
                    new ScrollView
                    {
                        Orientation = ScrollOrientation.Horizontal,
                        Content = new HorizontalStackLayout
                        {
                            Spacing = 8,
                            Children =
                            {
                                _keyboardToggleButton,
                                _specialKeysToggleButton,
                                qualityButton,
                                _monitorButton,
                                rebootButton,
                                disconnectButton
                            }
                        }
                    }
                }
            }
        };
    }

    private Border BuildQualityOverlay()
    {
        _qualityTitleLabel = new Label
        {
            Text = "Quality: --",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.TextMuted
        };

        _qualityDetailsLabel = new Label
        {
            Text = "Connect to see live quality.",
            FontSize = 11,
            TextColor = ThemeColors.TextMuted
        };

        return new Border
        {
            IsVisible = false,
            HorizontalOptions = LayoutOptions.End,
            MaximumWidthRequest = 260,
            BackgroundColor = ThemeColors.SurfaceBackgroundAlt,
            Stroke = ThemeColors.CardBorder,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(12, 8),
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(ThemeColors.ShadowColor),
                Opacity = 0.10f,
                Radius = 12,
                Offset = new Point(0, 4)
            },
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                Children = { _qualityTitleLabel, _qualityDetailsLabel }
            }
        };
    }

    private Border BuildMonitorSelectorPanel()
    {
        var refreshButton = new Button
        {
            Text = "Refresh",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = ThemeColors.SecondaryButtonBackground,
            TextColor = ThemeColors.SecondaryButtonText,
            CornerRadius = 14,
            HeightRequest = 32,
            Padding = new Thickness(12, 4)
        };
        refreshButton.Clicked += OnRefreshMonitorSelectorClicked;

        var doneButton = new Button
        {
            Text = "Done",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = ThemeColors.SecondaryButtonBackground,
            TextColor = ThemeColors.SecondaryButtonText,
            CornerRadius = 14,
            HeightRequest = 32,
            Padding = new Thickness(12, 4)
        };
        doneButton.Clicked += (_, _) => HideMonitorSelector();

        _monitorSelectorStatusLabel = new Label
        {
            Text = _monitorStatusText,
            FontSize = 12,
            TextColor = _monitorStatusColor
        };

        _monitorCarousel = new HorizontalStackLayout
        {
            Spacing = 10,
            Children = { BuildMonitorPlaceholder("Loading remote monitors...") }
        };

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        header.Add(new Label
        {
            Text = "Monitor selector",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.AccentText
        });
        header.Add(new HorizontalStackLayout
        {
            Spacing = 8,
            Children = { refreshButton, doneButton }
        }, 1, 0);

        return new Border
        {
            IsVisible = false,
            BackgroundColor = ThemeColors.CardBackground,
            Stroke = ThemeColors.ToolbarBorder,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 },
            Padding = new Thickness(12, 10),
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(ThemeColors.ShadowColor),
                Opacity = 0.12f,
                Radius = 16,
                Offset = new Point(0, 6)
            },
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    header,
                    _monitorSelectorStatusLabel,
                    new ScrollView
                    {
                        Orientation = ScrollOrientation.Horizontal,
                        Content = _monitorCarousel
                    }
                }
            }
        };
    }

    private View BuildMonitorPlaceholder(string text)
    {
        return new Border
        {
            WidthRequest = 220,
            MinimumHeightRequest = 138,
            BackgroundColor = ThemeColors.PlaceholderBackground,
            Stroke = ThemeColors.CardBorder,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(16),
            Content = new Label
            {
                Text = text,
                FontSize = 13,
                TextColor = ThemeColors.TextSecondary,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center
            }
        };
    }

    private void RebuildMonitorCarousel()
    {
        _monitorCarousel.Children.Clear();

        if (_remoteMonitors.Count == 0)
        {
            _monitorCarousel.Children.Add(BuildMonitorPlaceholder("No remote monitors available."));
            return;
        }

        foreach (var monitor in _remoteMonitors)
            _monitorCarousel.Children.Add(BuildMonitorCard(monitor));
    }

    private View BuildMonitorCard(MonitorInfo monitor)
    {
        bool isSelected = string.Equals(monitor.Id, _selectedMonitorId, StringComparison.Ordinal);

        var badgeRow = new HorizontalStackLayout { Spacing = 6 };
        if (monitor.IsPrimary)
            badgeRow.Children.Add(BuildMonitorBadge("Primary", ThemeColors.WarningSoft, ThemeColors.WarningText));
        if (isSelected)
            badgeRow.Children.Add(BuildMonitorBadge("Current", ThemeColors.SuccessBackground, ThemeColors.SuccessText));

        var card = new Border
        {
            WidthRequest = 220,
            BackgroundColor = isSelected ? ThemeColors.SelectedCardBackground : ThemeColors.CardBackground,
            Stroke = isSelected ? ThemeColors.SelectedCardBorder : ThemeColors.CardBorder,
            StrokeThickness = isSelected ? 2 : 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(12),
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    CreateMonitorPreview(monitor, isSelected),
                    new Label
                    {
                        Text = monitor.Name,
                        FontSize = 14,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = ThemeColors.TextPrimary,
                        LineBreakMode = LineBreakMode.TailTruncation
                    },
                    badgeRow,
                    new Label
                    {
                        Text = $"{monitor.Width} × {monitor.Height}",
                        FontSize = 12,
                        TextColor = ThemeColors.TextSecondary
                    },
                    new Label
                    {
                        Text = $"Position {monitor.Left}, {monitor.Top}",
                        FontSize = 11,
                        TextColor = ThemeColors.TextMuted
                    }
                }
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await SelectMonitorAsync(monitor);
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private View CreateMonitorPreview(MonitorInfo monitor, bool isSelected)
    {
        const double maxWidth = 156;
        const double maxHeight = 78;

        var scale = Math.Min(maxWidth / Math.Max(1, monitor.Width), maxHeight / Math.Max(1, monitor.Height));
        var previewWidth = Math.Max(54, monitor.Width * scale);
        var previewHeight = Math.Max(34, monitor.Height * scale);

        return new Grid
        {
            HeightRequest = 94,
            BackgroundColor = isSelected ? ThemeColors.Accent : ThemeColors.AccentSoft,
            Padding = new Thickness(12, 8),
            Children =
            {
                new Border
                {
                    WidthRequest = previewWidth,
                    HeightRequest = previewHeight,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    BackgroundColor = isSelected ? Color.FromArgb("#8E73E6") : Color.FromArgb("#D9CFFF"),
                    Stroke = isSelected ? Colors.White : ThemeColors.ToolbarBorder,
                    StrokeThickness = 1,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                    Content = new Label
                    {
                        Text = GetCompactMonitorName(monitor),
                        FontSize = 12,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = isSelected ? Colors.White : ThemeColors.Accent,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center,
                        HorizontalTextAlignment = TextAlignment.Center,
                        VerticalTextAlignment = TextAlignment.Center
                    }
                }
            }
        };
    }

    private static View BuildMonitorBadge(string text, Color backgroundColor, Color textColor)
    {
        return new Border
        {
            BackgroundColor = backgroundColor,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(8, 2),
            Content = new Label
            {
                Text = text,
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                TextColor = textColor
            }
        };
    }

    private static string GetCompactMonitorName(MonitorInfo monitor)
    {
        if (monitor.IsPrimary)
            return "Primary";

        var trimmed = monitor.Name.Trim();
        return trimmed.Length <= 12 ? trimmed : trimmed[..11] + "…";
    }

    private void UpdateMonitorButtonState()
    {
        if (_monitorButton == null)
            return;

        if (!_client.IsConnected)
        {
            _monitorButton.Text = "🖥 Monitor";
            _monitorButton.BackgroundColor = ThemeColors.Accent;
            return;
        }

        var selectedMonitor = _remoteMonitors.FirstOrDefault(m => string.Equals(m.Id, _selectedMonitorId, StringComparison.Ordinal));
        _monitorButton.Text = _isMonitorSelectorLoading
            ? "🖥 Loading..."
            : selectedMonitor != null
                ? $"🖥 {GetCompactMonitorName(selectedMonitor)}"
                : "🖥 Monitor";

        _monitorButton.BackgroundColor = _isMonitorSelectorVisible
            ? ThemeColors.Info
            : ThemeColors.Accent;
    }

    private void HideMonitorSelector()
    {
        if (!_isMonitorSelectorVisible)
            return;

        _isMonitorSelectorVisible = false;
        UpdateSessionOverlayVisibility();
        StatusMessage = "Monitor selector hidden.";
    }

    private async Task LoadRemoteMonitorsAsync()
    {
        _isMonitorSelectorLoading = true;
        UpdateMonitorButtonState();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            SetMonitorStatus("Loading remote monitors...", ThemeColors.TextSecondary);
            _monitorCarousel.Children.Clear();
            _monitorCarousel.Children.Add(BuildMonitorPlaceholder("Loading remote monitors..."));
        });

        try
        {
            var (monitors, selectedMonitorId) = await _client.GetRemoteMonitorsAsync();

            _remoteMonitors.Clear();
            foreach (var monitor in monitors.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Name))
                _remoteMonitors.Add(monitor);

            _selectedMonitorId = selectedMonitorId
                ?? _remoteMonitors.FirstOrDefault(m => m.IsPrimary)?.Id
                ?? _remoteMonitors.FirstOrDefault()?.Id;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                RebuildMonitorCarousel();
                SetMonitorStatus(
                    _remoteMonitors.Count == 0
                        ? "No monitors found on the remote host."
                        : "Swipe through the displays and tap one to switch instantly.",
                    _remoteMonitors.Count == 0 ? ThemeColors.TextSecondary : ThemeColors.TextSecondary);
                UpdateMonitorButtonState();
            });
        }
        finally
        {
            _isMonitorSelectorLoading = false;
            MainThread.BeginInvokeOnMainThread(UpdateMonitorButtonState);
        }
    }

    private async Task WarmMonitorSelectorAsync()
    {
        try
        {
            await LoadRemoteMonitorsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to preload remote monitors for the session toolbar");
        }
    }

    private async void OnRefreshMonitorSelectorClicked(object? sender, EventArgs e)
    {
        if (!_client.IsConnected || _isMonitorSelectorLoading)
            return;

        try
        {
            StatusMessage = "Refreshing remote monitor list...";
            await LoadRemoteMonitorsAsync();
            StatusMessage = _remoteMonitors.Count > 0
                ? "Monitor list refreshed. Tap a display card to switch."
                : "No monitors found on the remote host.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh remote monitor list");
            SetMonitorStatus($"Failed to refresh monitors: {ex.Message}", ThemeColors.Danger);
            await DisplayAlertAsync("Monitor", $"Failed to refresh remote monitors: {ex.Message}", "OK");
        }
    }

    private async Task SelectMonitorAsync(MonitorInfo monitor)
    {
        if (!_client.IsConnected || _isMonitorSelectorLoading)
            return;

        try
        {
            _isMonitorSelectorLoading = true;
            UpdateMonitorButtonState();
            SetMonitorStatus($"Switching to {monitor.Name}...", ThemeColors.Info);

            _selectedMonitorId = await _client.SelectRemoteMonitorAsync(monitor.Id);
            RebuildMonitorCarousel();
            SetMonitorStatus($"Switched to {monitor.Name}.", ThemeColors.SuccessText);
            StatusMessage = $"Remote monitor switched to {monitor.Name}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to switch remote monitor");
            SetMonitorStatus($"Failed to switch monitor: {ex.Message}", ThemeColors.Danger);
            await DisplayAlertAsync("Monitor", $"Failed to switch remote monitor: {ex.Message}", "OK");
        }
        finally
        {
            _isMonitorSelectorLoading = false;
            UpdateMonitorButtonState();
        }
    }

    private Border BuildSpecialKeysBar()
    {
        return new Border
        {
            IsVisible = false,
            BackgroundColor = ThemeColors.CardBackground,
            Stroke = ThemeColors.CardBorder,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(10, 8),
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(ThemeColors.ShadowColor),
                Opacity = 0.10f,
                Radius = 12,
                Offset = new Point(0, 4)
            },
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    new Label
                    {
                        Text = "Virtual keyboard",
                        FontSize = 11,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = ThemeColors.AccentText
                    },
                    BuildKeyRow(
                        BuildModifierButton("Ctrl"),
                        BuildModifierButton("Alt"),
                        BuildModifierButton("Shift"),
                        BuildModifierButton("Win"),
                        BuildSpecialKeyButton("Esc", async () => await SendKeyTapAsync("Escape")),
                        BuildSpecialKeyButton("Tab", async () => await SendKeyTapAsync("Tab"))),
                    BuildKeyRow(
                        BuildSpecialKeyButton("Enter", async () => await SendKeyTapAsync("Return")),
                        BuildSpecialKeyButton("⌫", async () => await SendKeyTapAsync("Back")),
                        BuildSpecialKeyButton("↑", async () => await SendKeyTapAsync("Up")),
                        BuildSpecialKeyButton("↓", async () => await SendKeyTapAsync("Down")),
                        BuildSpecialKeyButton("←", async () => await SendKeyTapAsync("Left")),
                        BuildSpecialKeyButton("→", async () => await SendKeyTapAsync("Right"))),
                    BuildKeyRow(BuildFunctionKeys(1, 6).ToArray()),
                    BuildKeyRow(BuildFunctionKeys(7, 6).ToArray()),
                    BuildKeyRow(
                        BuildSpecialKeyButton("Alt+Tab", async () => await SendShortcutAsync(KeyboardShortcut.TaskSwitcher)),
                        BuildSpecialKeyButton("Win+D", async () => await SendShortcutAsync(KeyboardShortcut.ShowDesktop)),
                        BuildSpecialKeyButton("Task Mgr", async () => await SendShortcutAsync(KeyboardShortcut.TaskManager)))
                }
            }
        };
    }

    private View BuildKeyRow(params View[] buttons)
    {
        var row = new HorizontalStackLayout
        {
            Spacing = 6
        };

        foreach (var button in buttons)
            row.Add(button);

        return new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            Content = row
        };
    }

    private IEnumerable<View> BuildFunctionKeys(int start, int count)
    {
        for (int i = start; i < start + count; i++)
        {
            int functionNumber = i;
            yield return BuildSpecialKeyButton($"F{functionNumber}", async () => await SendKeyTapAsync($"F{functionNumber}"));
        }
    }

    private Entry BuildKeyCaptureEntry()
    {
        var entry = new Entry
        {
            Opacity = 0.01,
            WidthRequest = 1,
            HeightRequest = 1,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End,
            BackgroundColor = Colors.Transparent,
            TextColor = Colors.Transparent,
            Placeholder = string.Empty,
            Keyboard = Keyboard.Text
        };

        entry.TextChanged += OnKeyCaptureTextChanged;
        return entry;
    }

    private Button BuildSessionActionButton(string text, EventHandler clicked, Color? backgroundColor = null)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = backgroundColor ?? ThemeColors.Accent,
            TextColor = Colors.White,
            CornerRadius = 20,
            HeightRequest = 40,
            Padding = new Thickness(14, 8)
        };

        button.Clicked += clicked;
        return button;
    }

    private Button BuildSpecialKeyButton(string text, Func<Task> onClick)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 12,
            BackgroundColor = ThemeColors.ChipBackground,
            TextColor = ThemeColors.ChipText,
            CornerRadius = 14,
            HeightRequest = 36,
            Padding = new Thickness(12, 6)
        };

        button.Clicked += async (_, _) => await onClick();
        return button;
    }

    private Button BuildModifierButton(string label)
    {
        var button = new Button
        {
            Text = label,
            FontSize = 12,
            BackgroundColor = ThemeColors.ChipBackground,
            TextColor = ThemeColors.ChipText,
            CornerRadius = 14,
            HeightRequest = 36,
            Padding = new Thickness(12, 6)
        };

        _modifierButtons[label] = button;
        button.Clicked += async (_, _) => await ToggleModifierAsync(label);
        return button;
    }

    private void UpdateSessionOverlayVisibility()
    {
        bool isConnected = _client.IsConnected;

        if (_qualityOverlay != null)
            _qualityOverlay.IsVisible = isConnected;

        if (_sessionToolbar != null)
            _sessionToolbar.IsVisible = isConnected;

        if (_specialKeysBar != null)
            _specialKeysBar.IsVisible = isConnected && _isSpecialKeysVisible;

        if (_monitorSelectorPanel != null)
            _monitorSelectorPanel.IsVisible = isConnected && _isMonitorSelectorVisible;

        if (!isConnected)
        {
            _isKeyboardVisible = false;
            _isSpecialKeysVisible = false;
            _isMonitorSelectorVisible = false;
            _isMonitorSelectorLoading = false;
            _activeModifiers.Clear();
            _remoteMonitors.Clear();
            ApplyConnectionQuality(null);

            if (_monitorCarousel != null)
            {
                _monitorCarousel.Children.Clear();
                _monitorCarousel.Children.Add(BuildMonitorPlaceholder("Connect to a host to browse monitors."));
            }

            if (_keyCaptureEntry != null)
            {
                _suppressKeyCaptureTextChanged = true;
                _keyCaptureEntry.Text = string.Empty;
                _suppressKeyCaptureTextChanged = false;
                _keyCaptureEntry.Unfocus();
            }
        }

        UpdateSessionActionButtonStates();
    }

    private void UpdateSessionActionButtonStates()
    {
        if (_keyboardToggleButton != null)
            _keyboardToggleButton.BackgroundColor = _isKeyboardVisible ? ThemeColors.Success : ThemeColors.Accent;

        if (_specialKeysToggleButton != null)
            _specialKeysToggleButton.BackgroundColor = _isSpecialKeysVisible ? Color.FromArgb("#7B1FA2") : ThemeColors.Accent;

        UpdateMonitorButtonState();
        UpdateModifierButtonStates();
    }

    private void UpdateModifierButtonStates()
    {
        foreach (var modifier in _modifierButtons)
        {
            bool isActive = _activeModifiers.Contains(modifier.Key);
            modifier.Value.BackgroundColor = isActive ? ThemeColors.Success : ThemeColors.ChipBackground;
            modifier.Value.TextColor = isActive ? Colors.White : ThemeColors.ChipText;
        }
    }

    private void ApplyConnectionQuality(ConnectionQuality? quality)
    {
        if (_qualityOverlay == null || _qualityTitleLabel == null || _qualityDetailsLabel == null)
            return;

        if (quality == null)
        {
            _qualityTitleLabel.Text = _client.IsConnected ? "Quality: Measuring..." : "Quality: --";
            _qualityDetailsLabel.Text = _client.IsConnected
                ? "Waiting for live connection metrics..."
                : "Connect to see live quality.";
            ApplyQualityPalette(null);
            return;
        }

        _qualityTitleLabel.Text = $"Quality: {quality.Rating}";
        _qualityDetailsLabel.Text = $"{Math.Round(quality.Fps):0} FPS • {quality.Latency} ms • {quality.GetBandwidthString()}";
        ApplyQualityPalette(quality.Rating);
    }

    private void ApplyQualityPalette(QualityRating? rating)
    {
        var (background, border, text) = ThemeColors.GetQualityPalette(rating);

        _qualityOverlay.BackgroundColor = background;
        _qualityOverlay.Stroke = border;
        _qualityTitleLabel.TextColor = text;
        _qualityDetailsLabel.TextColor = text;
    }

    private async void OnKeyboardToggleClicked(object? sender, EventArgs e)
    {
        if (!_client.IsConnected) return;

        _isKeyboardVisible = !_isKeyboardVisible;
        if (_isKeyboardVisible)
        {
            _isSpecialKeysVisible = true;
            UpdateSessionOverlayVisibility();
            StatusMessage = "Keyboard ready — type to send text to the remote host.";

            await Task.Delay(50);
            MainThread.BeginInvokeOnMainThread(() => _keyCaptureEntry.Focus());
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(() => _keyCaptureEntry.Unfocus());
            StatusMessage = "Keyboard hidden.";
            UpdateSessionOverlayVisibility();
        }
    }

    private void OnSpecialKeysToggleClicked(object? sender, EventArgs e)
    {
        if (!_client.IsConnected) return;

        _isSpecialKeysVisible = !_isSpecialKeysVisible;
        UpdateSessionOverlayVisibility();
        StatusMessage = _isSpecialKeysVisible
            ? "Special keys bar shown."
            : "Special keys bar hidden.";
    }

    private async void OnQualityClicked(object? sender, EventArgs e)
    {
        if (!_client.IsConnected) return;

        try
        {
            var presets = new[]
            {
                (Label: "Low (50%)", Quality: 50),
                (Label: "Medium (65%)", Quality: 65),
                (Label: "High (75%)", Quality: 75),
                (Label: "Ultra (85%)", Quality: 85)
            };

            var options = presets
                .Select(p => p.Quality == _selectedQuality ? $"{p.Label} ✓" : p.Label)
                .ToArray();

            var choice = await DisplayActionSheetAsync("Display Quality", "Cancel", null, options);
            if (string.IsNullOrWhiteSpace(choice) || choice == "Cancel")
                return;

            int index = Array.IndexOf(options, choice);
            if (index < 0)
                return;

            _selectedQuality = await _client.SetRemoteQualityAsync(presets[index].Quality);
            StatusMessage = $"Remote quality set to {_selectedQuality}% ({presets[index].Label}).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set remote quality");
            await DisplayAlertAsync("Quality", $"Failed to set remote quality: {ex.Message}", "OK");
        }
    }

    private async void OnMonitorClicked(object? sender, EventArgs e)
    {
        if (!_client.IsConnected) return;

        try
        {
            if (_isMonitorSelectorVisible && !_isMonitorSelectorLoading)
            {
                HideMonitorSelector();
                return;
            }

            _isSpecialKeysVisible = false;
            _isMonitorSelectorVisible = true;
            UpdateSessionOverlayVisibility();
            await LoadRemoteMonitorsAsync();
            StatusMessage = _remoteMonitors.Count > 0
                ? "Monitor selector ready — tap a display card to switch."
                : "No monitors found on the remote host.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to switch remote monitor");
            SetMonitorStatus($"Failed to load monitors: {ex.Message}", ThemeColors.Danger);
            await DisplayAlertAsync("Monitor", $"Failed to switch remote monitor: {ex.Message}", "OK");
        }
    }

    private async void OnToolbarDisconnectClicked(object? sender, EventArgs e)
    {
        if (!_client.IsConnected) return;

        bool confirm = await DisplayAlertAsync(
            "Disconnect",
            $"End the session with {_client.ConnectedHost?.DeviceName ?? "the remote host"}?",
            "Disconnect",
            "Cancel");

        if (confirm)
            await DisconnectAsync();
    }

    private async void OnRemoteRebootClicked(object? sender, EventArgs e)
    {
        if (!_client.IsConnected) return;

        bool confirm = await DisplayAlertAsync(
            "Reboot Remote Device",
            $"Restart {_client.ConnectedHost?.DeviceName ?? "the remote host"}? RemoteLink will try to reconnect automatically when supported.",
            "Reboot",
            "Cancel");

        if (!confirm)
            return;

        try
        {
            var response = await _client.RequestRemoteRebootAsync();
            StatusMessage = response.AutoReconnectSupported == true
                ? $"Remote reboot requested. Waiting {response.ReconnectDelaySeconds ?? 25}s before reconnecting..."
                : "Remote reboot requested. Automatic reconnect is unavailable for this session.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to request remote reboot");
            await DisplayAlertAsync("Remote Reboot", $"Failed to request remote reboot: {ex.Message}", "OK");
        }
    }

    private void OnKeyCaptureTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressKeyCaptureTextChanged || !_client.IsConnected) return;

        var newText = e.NewTextValue ?? string.Empty;
        var oldText = e.OldTextValue ?? string.Empty;

        if (string.IsNullOrEmpty(newText))
        {
            if (!string.IsNullOrEmpty(oldText))
                _ = SendKeyTapAsync("Back");

            return;
        }

        var addedText = newText.Length > oldText.Length
            ? newText[oldText.Length..]
            : newText;

        foreach (var ch in addedText)
        {
            _ = SendCapturedCharacterAsync(ch);
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _suppressKeyCaptureTextChanged = true;
            _keyCaptureEntry.Text = string.Empty;
            _suppressKeyCaptureTextChanged = false;
        });
    }

    private async Task SendKeyTapAsync(string keyCode)
    {
        if (!_client.IsConnected) return;

        await _client.SendInputEventAsync(new InputEvent
        {
            Type = InputEventType.KeyPress,
            KeyCode = keyCode,
            IsPressed = true
        });

        await _client.SendInputEventAsync(new InputEvent
        {
            Type = InputEventType.KeyRelease,
            KeyCode = keyCode,
            IsPressed = false
        });

        await ReleaseActiveModifiersAsync();
    }

    private async Task SendShortcutAsync(KeyboardShortcut shortcut)
    {
        if (!_client.IsConnected)
            return;

        await ReleaseActiveModifiersAsync();

        await _client.SendInputEventAsync(new InputEvent
        {
            Type = InputEventType.KeyboardShortcut,
            Shortcut = shortcut
        });
    }

    private async Task ToggleModifierAsync(string label)
    {
        if (!_client.IsConnected || !_modifierKeyCodes.TryGetValue(label, out var keyCode))
            return;

        bool activating = !_activeModifiers.Contains(label);
        if (activating)
        {
            _activeModifiers.Add(label);
            await _client.SendInputEventAsync(new InputEvent
            {
                Type = InputEventType.KeyPress,
                KeyCode = keyCode,
                IsPressed = true
            });
            StatusMessage = $"{label} active for the next key.";
        }
        else
        {
            _activeModifiers.Remove(label);
            await _client.SendInputEventAsync(new InputEvent
            {
                Type = InputEventType.KeyRelease,
                KeyCode = keyCode,
                IsPressed = false
            });
            StatusMessage = $"{label} released.";
        }

        MainThread.BeginInvokeOnMainThread(UpdateModifierButtonStates);
    }

    private async Task ReleaseActiveModifiersAsync()
    {
        if (!_client.IsConnected || _activeModifiers.Count == 0)
            return;

        foreach (var modifier in _activeModifiers.ToArray())
        {
            if (!_modifierKeyCodes.TryGetValue(modifier, out var keyCode))
                continue;

            await _client.SendInputEventAsync(new InputEvent
            {
                Type = InputEventType.KeyRelease,
                KeyCode = keyCode,
                IsPressed = false
            });
        }

        _activeModifiers.Clear();
        MainThread.BeginInvokeOnMainThread(UpdateModifierButtonStates);
    }

    private async Task SendCapturedCharacterAsync(char ch)
    {
        if (!_client.IsConnected)
            return;

        if (_activeModifiers.Count > 0 && TryMapCharacterToKeyCode(ch, out var keyCode))
        {
            await SendKeyTapAsync(keyCode);
            return;
        }

        if (_activeModifiers.Count > 0)
        {
            StatusMessage = "Modifier keys apply to letters, digits, and space in the text overlay.";
            await ReleaseActiveModifiersAsync();
        }

        await _client.SendInputEventAsync(new InputEvent
        {
            Type = InputEventType.TextInput,
            Text = ch.ToString()
        });
    }

    private static bool TryMapCharacterToKeyCode(char ch, out string keyCode)
    {
        if (char.IsLetter(ch))
        {
            keyCode = char.ToUpperInvariant(ch).ToString();
            return true;
        }

        if (char.IsDigit(ch))
        {
            keyCode = $"D{ch}";
            return true;
        }

        if (ch == ' ')
        {
            keyCode = "Space";
            return true;
        }

        keyCode = string.Empty;
        return false;
    }

    // ── Manual connection card ──────────────────────────────────────────

    private Border BuildManualConnectCard()
    {
        var card = new Border
        {
            BackgroundColor = ThemeColors.CardBackgroundAlt,
            Stroke = ThemeColors.Accent,
            StrokeThickness = 1.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(16),
            Margin = new Thickness(0, 4, 0, 0)
        };

        var stack = new StackLayout { Spacing = 10 };

        // Section header
        stack.Add(new Label
        {
            Text = "Connect to Remote Host",
            FontSize = 17,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.Accent
        });

        stack.Add(new Label
        {
            Text = "Enter the Partner ID and PIN displayed on the desktop host.",
            FontSize = 12,
            TextColor = ThemeColors.TextSecondary,
            Margin = new Thickness(0, 0, 0, 4)
        });

        // Partner ID entry
        stack.Add(new Label
        {
            Text = "Partner ID",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.TextPrimary
        });

        _partnerIdEntry = new Entry
        {
            Placeholder = "IP address, IP:Port, or 9-digit ID",
            FontSize = 15,
            Keyboard = Keyboard.Text,
            BackgroundColor = ThemeColors.InputBackground,
            TextColor = ThemeColors.TextPrimary,
            HeightRequest = 44,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing
        };
        _partnerIdEntry.TextChanged += OnManualEntryChanged;
        stack.Add(_partnerIdEntry);

        // PIN entry
        stack.Add(new Label
        {
            Text = "PIN",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.TextPrimary,
            Margin = new Thickness(0, 4, 0, 0)
        });

        _pinEntry = new Entry
        {
            Placeholder = "6-digit PIN",
            FontSize = 15,
            Keyboard = Keyboard.Numeric,
            MaxLength = 6,
            IsPassword = true,
            BackgroundColor = ThemeColors.InputBackground,
            TextColor = ThemeColors.TextPrimary,
            HeightRequest = 44
        };
        _pinEntry.TextChanged += OnManualEntryChanged;
        stack.Add(_pinEntry);

        // Connect button
        _manualConnectButton = new Button
        {
            Text = "Connect",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = ThemeColors.Accent,
            TextColor = Colors.White,
            CornerRadius = 8,
            HeightRequest = 48,
            IsEnabled = false,
            Margin = new Thickness(0, 4, 0, 0)
        };
        _manualConnectButton.Clicked += OnManualConnectClicked;
        stack.Add(_manualConnectButton);

        // Status label (for feedback)
        _manualStatusLabel = new Label
        {
            FontSize = 13,
            HorizontalTextAlignment = TextAlignment.Center,
            IsVisible = false,
            Margin = new Thickness(0, 2, 0, 0)
        };
        stack.Add(_manualStatusLabel);

        card.Content = stack;
        return card;
    }

    private void OnManualEntryChanged(object? sender, TextChangedEventArgs e)
    {
        var hasId = !string.IsNullOrWhiteSpace(_partnerIdEntry?.Text);
        var hasPin = (_pinEntry?.Text?.Length ?? 0) == 6;

        if (_manualConnectButton != null)
            _manualConnectButton.IsEnabled = hasId && hasPin && !_isManualConnecting;
    }

    private async void OnManualConnectClicked(object? sender, EventArgs e)
    {
        var partnerId = _partnerIdEntry?.Text?.Trim();
        var pin = _pinEntry?.Text?.Trim();

        if (string.IsNullOrWhiteSpace(partnerId) || string.IsNullOrWhiteSpace(pin))
            return;

        // Resolve the partner ID to a DeviceInfo
        var targetDevice = ResolvePartner(partnerId);
        if (targetDevice == null)
        {
            SetManualStatus("Invalid Partner ID. Use IP, IP:Port, or 9-digit ID.", ThemeColors.Danger);
            return;
        }

        _isManualConnecting = true;
        SetManualConnectButtonState("Connecting...", ThemeColors.NeutralButtonBackground, false);
        SetManualStatus($"Connecting to {targetDevice.IPAddress}:{targetDevice.Port}...", ThemeColors.WarningText);
        StatusMessage = $"Connecting to {targetDevice.DeviceName}...";
        IsDiscovering = true;

        var success = await _client.ConnectToHostAsync(targetDevice, pin);

        IsDiscovering = false;

        if (success)
        {
            _connectionStartedAt = DateTime.UtcNow;
            await RecordConnectionAsync(targetDevice, ConnectionOutcome.Success);
            SetManualStatus("Connected!", ThemeColors.SuccessText);
            // Button stays disabled while connected; will reset on disconnect via OnAppearing
        }
        else
        {
            await RecordConnectionAsync(targetDevice, ConnectionOutcome.Failed);
            _isManualConnecting = false;
            SetManualConnectButtonState("Connect", ThemeColors.Accent, true);
            SetManualStatus("Connection failed. Check Partner ID and PIN.", ThemeColors.Danger);
            OnManualEntryChanged(null, null!); // Re-evaluate button enabled state
        }
    }

    private void SetManualConnectButtonState(string text, Color bgColor, bool enabled)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_manualConnectButton != null)
            {
                _manualConnectButton.Text = text;
                _manualConnectButton.BackgroundColor = bgColor;
                _manualConnectButton.IsEnabled = enabled;
            }
        });
    }

    private void SetManualStatus(string message, Color color)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _manualStatusText = message;
            _manualStatusColor = color;
            _manualStatusVisible = true;
            if (_manualStatusLabel != null)
            {
                _manualStatusLabel.Text = message;
                _manualStatusLabel.TextColor = color;
                _manualStatusLabel.IsVisible = true;
            }
        });
    }

    private void SetMonitorStatus(string message, Color color)
    {
        _monitorStatusText = message;
        _monitorStatusColor = color;

        if (_monitorSelectorStatusLabel != null)
        {
            _monitorSelectorStatusLabel.Text = message;
            _monitorSelectorStatusLabel.TextColor = color;
        }
    }

    /// <summary>
    /// Resolves a Partner ID string into a DeviceInfo.
    /// Accepts: 9-digit numeric ID (matched against discovered hosts),
    /// IP:Port (e.g. "192.168.1.5:12346"), or plain IP (defaults to port 12346).
    /// </summary>
    private DeviceInfo? ResolvePartner(string partnerId)
    {
        var stripped = DeviceIdentityManager.NormalizeInternetDeviceId(partnerId);

        // Try to match against discovered hosts by numeric ID
        if (stripped is not null)
        {
            foreach (var host in _availableHosts)
            {
                var hostNumericId = DeviceIdentityManager.NormalizeInternetDeviceId(host.InternetDeviceId)
                    ?? DeviceIdentityManager.NormalizeInternetDeviceId(DeviceIdentityManager.GenerateLegacyNumericId(host.DeviceName));
                if (hostNumericId == stripped)
                    return host;
            }

            return new DeviceInfo
            {
                DeviceId = stripped,
                InternetDeviceId = stripped,
                DeviceName = DeviceIdentityManager.FormatInternetDeviceId(stripped),
                Type = DeviceType.Desktop,
                SupportsRelay = true
            };
        }

        // Try IP:Port format
        if (partnerId.Contains(':'))
        {
            var parts = partnerId.Split(':', 2);
            if (parts.Length == 2 && int.TryParse(parts[1], out int port) && port is > 0 and <= 65535)
            {
                return new DeviceInfo
                {
                    DeviceId = $"manual_{parts[0]}_{port}",
                    DeviceName = parts[0],
                    IPAddress = parts[0],
                    Port = port,
                    Type = DeviceType.Desktop
                };
            }
        }

        // Try plain IP (use default port 12346)
        if (System.Net.IPAddress.TryParse(partnerId, out _))
        {
            return new DeviceInfo
            {
                DeviceId = $"manual_{partnerId}_12346",
                DeviceName = partnerId,
                IPAddress = partnerId,
                Port = 12346,
                Type = DeviceType.Desktop
            };
        }

        return null;
    }

    // ── QR code scanner ────────────────────────────────────────────────

    private View BuildScanQrButton()
    {
        var button = new Button
        {
            Text = "\ud83d\udcf7  Scan QR Code",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = ThemeColors.Accent,
            TextColor = Colors.White,
            CornerRadius = 8,
            HeightRequest = 48,
            Margin = new Thickness(0, 4, 0, 0)
        };
        button.Clicked += OnScanQrClicked;
        return button;
    }

    private async void OnScanQrClicked(object? sender, EventArgs e)
    {
        var scannerPage = new QrScannerPage();
        scannerPage.QrCodeScanned += OnQrCodeScanned;
        await Navigation.PushModalAsync(scannerPage);
    }

    private void OnQrCodeScanned(object? sender, string qrData)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            // Close the scanner
            await Navigation.PopModalAsync();

            // Parse: remotelink://connect?host=IP:PORT&pin=PIN
            if (TryParseQrPayload(qrData, out var host, out var pin))
            {
                _partnerIdEntry.Text = host;
                _pinEntry.Text = pin;
                SetManualStatus("QR code scanned — tap Connect", ThemeColors.SuccessText);
            }
            else
            {
                SetManualStatus("Invalid QR code format.", ThemeColors.Danger);
            }
        });
    }

    private static bool TryParseQrPayload(string data, out string host, out string pin)
    {
        host = "";
        pin = "";

        try
        {
            if (!data.StartsWith("remotelink://connect", StringComparison.OrdinalIgnoreCase))
                return false;

            var uri = new Uri(data);
            var query = HttpUtility.ParseQueryString(uri.Query);
            host = query["host"] ?? "";
            pin = query["pin"] ?? "";

            return !string.IsNullOrWhiteSpace(host) && pin.Length == 6;
        }
        catch
        {
            return false;
        }
    }

    // ── Discovered hosts section ───────────────────────────────────────

    private View BuildDiscoveredHostsSection()
    {
        var section = new StackLayout { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

        // Separator
        section.Add(new StackLayout
        {
            Orientation = StackOrientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new BoxView
                {
                    Color = ThemeColors.Divider,
                    HeightRequest = 1,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Fill
                },
                new Label
                {
                    Text = "or quick connect",
                    FontSize = 12,
                    TextColor = ThemeColors.TextSecondary,
                    VerticalOptions = LayoutOptions.Center
                },
                new BoxView
                {
                    Color = ThemeColors.Divider,
                    HeightRequest = 1,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Fill
                }
            }
        });

        section.Add(new Label
        {
            Text = "Discovered Hosts",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.TextPrimary,
            Margin = new Thickness(0, 4, 0, 0)
        });

        _noHostsLabel = new Label
        {
            Text = "Scanning for desktop hosts...",
            FontSize = 13,
            TextColor = ThemeColors.TextSecondary,
            Margin = new Thickness(4, 0)
        };

        _hostListContainer = new StackLayout { Spacing = 8 };
        _hostListContainer.Add(_noHostsLabel);
        section.Add(_hostListContainer);

        return section;
    }

    // ── Host cards ─────────────────────────────────────────────────────

    private void AddHostCard(DeviceInfo device)
    {
        _hostListContainer.Remove(_noHostsLabel);

        var card = new Border
        {
            BackgroundColor = ThemeColors.CardBackground,
            Stroke = ThemeColors.CardBorder,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(12),
            AutomationId = $"host-{device.DeviceId}"
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            VerticalOptions = LayoutOptions.Center
        };

        var infoStack = new StackLayout { Spacing = 2 };
        infoStack.Add(new Label
        {
            Text = device.DeviceName,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.TextPrimary
        });
        infoStack.Add(new Label
        {
            Text = $"{device.IPAddress}:{device.Port}",
            FontSize = 12,
            TextColor = ThemeColors.TextSecondary
        });

        var formattedInternetId = DeviceIdentityManager.FormatInternetDeviceId(device.InternetDeviceId);
        if (!string.IsNullOrWhiteSpace(formattedInternetId))
        {
            infoStack.Add(new Label
            {
                Text = $"ID: {formattedInternetId}",
                FontSize = 11,
                TextColor = ThemeColors.Accent
            });
        }

        var connectLabel = new Label
        {
            Text = "Connect >",
            FontSize = 13,
            TextColor = ThemeColors.Accent,
            VerticalOptions = LayoutOptions.Center
        };
        Grid.SetColumn(connectLabel, 1);

        grid.Add(infoStack);
        grid.Add(connectLabel);
        card.Content = grid;

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OnHostCardTappedAsync(device);
        card.GestureRecognizers.Add(tap);

        _hostListContainer.Add(card);
    }

    private void RemoveHostCard(DeviceInfo device)
    {
        var toRemove = _hostListContainer.Children
            .OfType<Border>()
            .FirstOrDefault(f => f.AutomationId == $"host-{device.DeviceId}");

        if (toRemove != null)
            _hostListContainer.Remove(toRemove);

        if (!_hostListContainer.Children.OfType<Border>().Any())
            _hostListContainer.Add(_noHostsLabel);
    }

    // ── Connection flow (discovered host tap) ──────────────────────────

    private async Task OnHostCardTappedAsync(DeviceInfo host)
    {
        if (_client.IsConnected && _client.ConnectedHost?.DeviceId == host.DeviceId)
        {
            await DisplayAlertAsync("Already Connected",
                $"You are already connected to {host.DeviceName}.", "OK");
            return;
        }

        var pin = await DisplayPromptAsync(
            title: $"Connect to {host.DeviceName}",
            message: "Enter the 6-digit PIN shown on the desktop host:",
            accept: "Connect",
            cancel: "Cancel",
            placeholder: "123456",
            maxLength: 6,
            keyboard: Keyboard.Numeric);

        if (string.IsNullOrWhiteSpace(pin)) return;

        StatusMessage = $"Connecting to {host.DeviceName}...";
        IsDiscovering = true;

        var success = await _client.ConnectToHostAsync(host, pin);

        IsDiscovering = false;

        if (success)
        {
            _connectionStartedAt = DateTime.UtcNow;
            await RecordConnectionAsync(host, ConnectionOutcome.Success);
        }
        else
        {
            await RecordConnectionAsync(host, ConnectionOutcome.Failed);
            await DisplayAlertAsync("Connection Failed", StatusMessage, "OK");
        }
    }

    private async Task DisconnectAsync()
    {
        await _client.DisconnectAsync();
    }

    // ── Client event handlers ──────────────────────────────────────────

    private void OnDeviceDiscovered(object? sender, DeviceInfo device)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var existing = _availableHosts.FirstOrDefault(h => h.DeviceId == device.DeviceId);
            if (existing == null)
            {
                _availableHosts.Add(device);
                AddHostCard(device);
            }
            else
            {
                var index = _availableHosts.IndexOf(existing);
                _availableHosts[index] = device;
                RemoveHostCard(existing);
                AddHostCard(device);
            }

            StatusMessage = $"Found {_availableHosts.Count} host(s). Tap to connect.";
        });
    }

    private void OnDeviceLost(object? sender, DeviceInfo device)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var existing = _availableHosts.FirstOrDefault(h => h.DeviceId == device.DeviceId);
            if (existing != null)
            {
                _availableHosts.Remove(existing);
                RemoveHostCard(device);
                StatusMessage = _availableHosts.Count > 0
                    ? $"Found {_availableHosts.Count} host(s). Tap to connect."
                    : "Scanning for desktop hosts...";
            }
        });
    }

    private void OnConnectionStateChanged(object? sender, ClientConnectionState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            switch (state)
            {
                case ClientConnectionState.Connected:
                    var hostName = _client.ConnectedHost?.DeviceName ?? "Unknown";
                    _hasConnectedSession = true;
                    _connectedHostLabel.Text = $"Connected to {hostName}";
                    _connectedBanner.IsVisible = true;
                    _remoteViewer.IsVisible = true;
                    _manualConnectCard.IsVisible = false;
                    _scanQrButton.IsVisible = false;
                    _discoveredSection.IsVisible = false;
                    StatusMessage = $"Connected to {hostName}";
                    ApplyConnectionQuality(_client.CurrentConnectionQuality);
                    UpdateSessionOverlayVisibility();
                    _ = ApplyPreferredSessionSettingsAsync(hostName);
                    _ = ShowConnectionNotificationAsync("Connected", $"Connected to {hostName}.");
                    _ = WarmMonitorSelectorAsync();
                    break;

                case ClientConnectionState.Disconnected:
                    if (_client.IsAutoReconnectPending)
                    {
                        _connectedBanner.IsVisible = true;
                        _remoteViewer.IsVisible = true;
                        _manualConnectCard.IsVisible = false;
                        _scanQrButton.IsVisible = false;
                        _discoveredSection.IsVisible = false;
                        _connectedHostLabel.Text = $"Reconnecting to {_client.ConnectedHost?.DeviceName ?? "remote host"} after reboot...";
                        ApplyConnectionQuality(null);
                        UpdateSessionOverlayVisibility();
                        return;
                    }

                    var hadConnectedSession = _hasConnectedSession;
                    _hasConnectedSession = false;
                    if (_connectionStartedAt.HasValue)
                    {
                        var records = _connectionHistory.GetAll();
                        var lastRecord = records.FirstOrDefault();
                        if (lastRecord != null && lastRecord.Outcome == ConnectionOutcome.Success && lastRecord.DisconnectedAt == null)
                        {
                            lastRecord.DisconnectedAt = DateTime.UtcNow;
                            lastRecord.Duration = DateTime.UtcNow - _connectionStartedAt.Value;
                            lastRecord.Outcome = ConnectionOutcome.Disconnected;
                            _ = _connectionHistory.SaveAsync();
                        }
                        _connectionStartedAt = null;
                    }
                    _connectedBanner.IsVisible = false;
                    _remoteViewer.IsVisible = false;
                    _manualConnectCard.IsVisible = true;
                    _scanQrButton.IsVisible = true;
                    _discoveredSection.IsVisible = true;
                    _isManualConnecting = false;
                    SetManualConnectButtonState("Connect", ThemeColors.Accent, true);
                    _manualStatusVisible = false;
                    _manualStatusText = string.Empty;
                    _manualStatusLabel.IsVisible = false;
                    OnManualEntryChanged(null, null!);
                    _selectedMonitorId = null;
                    _selectedQuality = 75;
                    SetMonitorStatus("Loading remote monitors...", ThemeColors.TextSecondary);
                    ApplyConnectionQuality(null);
                    UpdateSessionOverlayVisibility();
                    if (StatusMessage.StartsWith("Connected"))
                        StatusMessage = "Disconnected. Scanning for hosts...";

                    if (hadConnectedSession)
                        _ = ShowConnectionNotificationAsync("Disconnected", "Remote session ended.");

                    break;

                case ClientConnectionState.Connecting:
                    StatusMessage = $"Connecting to {_client.ConnectedHost?.DeviceName}...";
                    break;

                case ClientConnectionState.Authenticating:
                    StatusMessage = "Authenticating...";
                    break;
            }
        });
    }

    private void OnPairingFailed(object? sender, string reason)
    {
        MainThread.BeginInvokeOnMainThread(() => StatusMessage = $"Pairing failed: {reason}");
    }

    private void OnConnectionQualityUpdated(object? sender, ConnectionQuality quality)
    {
        MainThread.BeginInvokeOnMainThread(() => ApplyConnectionQuality(quality));
    }

    private async Task ApplyPreferredSessionSettingsAsync(string hostName)
    {
        if (!_client.IsConnected)
            return;

        try
        {
            if (!_adaptiveQualityEnabled)
                _selectedQuality = await _client.SetRemoteQualityAsync(_selectedQuality);

            _preferredImageFormat = await _client.SetRemoteImageFormatAsync(_preferredImageFormat);
            _audioStreamingEnabled = await _client.SetRemoteAudioEnabledAsync(_audioStreamingEnabled);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                var qualityText = _adaptiveQualityEnabled ? "adaptive quality" : $"quality {_selectedQuality}%";
                StatusMessage = $"Connected to {hostName} • {qualityText} • {_preferredImageFormat} • Audio {(_audioStreamingEnabled ? "on" : "off")}.";
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply preferred session quality");
        }
    }

    private Task ShowConnectionNotificationAsync(string title, string message)
    {
        if (!_showConnectionNotifications)
            return Task.CompletedTask;

        return DisplayAlertAsync(title, message, "OK");
    }

    private void OnServiceStatusChanged(object? sender, string status)
    {
        MainThread.BeginInvokeOnMainThread(() => StatusMessage = status);
    }

    private void OnScreenDataReceived(object? sender, ScreenData screenData)
    {
        if (_frameRenderBusy) return;
        _frameRenderBusy = true;

        var stream = ScreenFrameConverter.ToImageStream(screenData);
        if (stream is null)
        {
            _frameRenderBusy = false;
            return;
        }

        if (screenData.Width > 0) _desktopWidth = screenData.Width;
        if (screenData.Height > 0) _desktopHeight = screenData.Height;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                _remoteViewer.Source = ImageSource.FromStream(() => stream);
            }
            finally
            {
                _frameRenderBusy = false;
            }
        });
    }

    // ── Gesture recognizers ────────────────────────────────────────────

    private void AttachGestureRecognizers(View surface)
    {
        var tap = new TapGestureRecognizer { NumberOfTapsRequired = 1 };
        tap.Tapped += OnTapped;
        surface.GestureRecognizers.Add(tap);

        var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        doubleTap.Tapped += OnDoubleTapped;
        surface.GestureRecognizers.Add(doubleTap);

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanned;
        surface.GestureRecognizers.Add(pan);

        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnScrolled;
        surface.GestureRecognizers.Add(pinch);
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not View surface) return;
        var pos = e.GetPosition(surface) ?? new Point(0, 0);
        ForwardGesture(new TouchGestureData
        {
            GestureType = TouchGestureType.Tap,
            X = (float)pos.X, Y = (float)pos.Y,
            DisplayWidth = (float)surface.Width, DisplayHeight = (float)surface.Height
        });
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not View surface) return;
        var pos = e.GetPosition(surface) ?? new Point(0, 0);
        ForwardGesture(new TouchGestureData
        {
            GestureType = TouchGestureType.DoubleTap,
            X = (float)pos.X, Y = (float)pos.Y,
            DisplayWidth = (float)surface.Width, DisplayHeight = (float)surface.Height
        });
    }

    private float _panStartX, _panStartY;

    private void OnPanned(object? sender, PanUpdatedEventArgs e)
    {
        if (sender is not View surface) return;
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartX = (float)(surface.Width / 2);
                _panStartY = (float)(surface.Height / 2);
                break;
            case GestureStatus.Running:
                var scaledTotalX = (float)e.TotalX * _gestureSensitivity;
                var scaledTotalY = (float)e.TotalY * _gestureSensitivity;
                ForwardGesture(new TouchGestureData
                {
                    GestureType = TouchGestureType.Pan,
                    X = _panStartX + scaledTotalX,
                    Y = _panStartY + scaledTotalY,
                    DeltaX = scaledTotalX, DeltaY = scaledTotalY,
                    DisplayWidth = (float)surface.Width, DisplayHeight = (float)surface.Height
                });
                break;
        }
    }

    private void OnScrolled(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status != GestureStatus.Running || sender is not View surface) return;
        float pixelDelta = (float)((1.0 - e.Scale) * 80.0 * _gestureSensitivity);
        ForwardGesture(new TouchGestureData
        {
            GestureType = TouchGestureType.Scroll,
            X = (float)(e.ScaleOrigin.X * surface.Width),
            Y = (float)(e.ScaleOrigin.Y * surface.Height),
            DeltaY = pixelDelta,
            DisplayWidth = (float)surface.Width, DisplayHeight = (float)surface.Height
        });
    }

    private void ForwardGesture(TouchGestureData gesture)
    {
        if (!_client.IsConnected) return;
        var events = _touchTranslator.Translate(gesture, _desktopWidth, _desktopHeight);
        foreach (var inputEvent in events)
            _ = _client.SendInputEventAsync(inputEvent);
    }

    // ── Connection history ─────────────────────────────────────────────

    private async Task RecordConnectionAsync(DeviceInfo device, ConnectionOutcome outcome)
    {
        var record = new ConnectionRecord
        {
            DeviceName = device.DeviceName,
            DeviceId = device.DeviceId,
            IPAddress = device.IPAddress,
            Port = device.Port,
            ConnectedAt = DateTime.UtcNow,
            Outcome = outcome
        };
        await _connectionHistory.AddAsync(record);
    }

    // ── INotifyPropertyChanged ─────────────────────────────────────────

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected override void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
