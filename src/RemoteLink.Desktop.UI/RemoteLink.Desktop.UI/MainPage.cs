using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using QRCoder;
using RemoteLink.Desktop.Services;
using RemoteLink.Desktop.UI.Services;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using DeviceInfo = RemoteLink.Shared.Models.DeviceInfo;
using DeviceType = RemoteLink.Shared.Models.DeviceType;

namespace RemoteLink.Desktop.UI;

public class MainPage : ContentPage, INotifyPropertyChanged
{
    private readonly ILogger<MainPage> _logger;
    private readonly RemoteDesktopHost _host;
    private readonly IPairingService _pairing;
    private readonly ICommunicationService _communication;
    private readonly INetworkDiscovery _networkDiscovery;
    private readonly IInputHandler _inputHandler;
    private readonly IPerformanceMonitor _perfMonitor;
    private readonly ISessionManager _sessionManager;
    private readonly RemoteDesktopClient _client;
    private readonly WindowsSystemTrayService _trayService;
    private readonly IAppSettingsService _appSettings;
    private readonly Func<SettingsPage> _settingsPageFactory;
    private readonly IFileTransferService _fileTransfer;
    private readonly ISessionRecorder _sessionRecorder;
    private readonly IScreenCapture _screenCapture;
    private readonly IMessagingService _messaging;
    private readonly Func<ChatPage> _chatPageFactory;
    private readonly Func<RemoteViewerPage> _viewerPageFactory;

    private CancellationTokenSource? _hostCts;
    private IDispatcherTimer? _pinExpiryTimer;
    private IDispatcherTimer? _metricsTimer;

    // UI state — host side
    private string _currentPin = "------";
    private string _statusText = "Stopped";
    private Color _statusColor = Colors.Gray;
    private bool _isRunning;
    private string _connectionInfo = "No active connections";
    private bool _pinVisible;
    private string _deviceNumericId;

    // UI state — client (partner connection) side
    private bool _isConnecting;
    private int _activeConnectionCount;
    private readonly List<DeviceInfo> _discoveredHosts = new();

    // UI element references — host panel
    private Label? _pinLabel;
    private Label? _statusLabel;
    private Label? _connectionLabel;
    private Button? _startStopButton;
    private Label? _deviceIdLabel;
    private BoxView? _statusIndicator;
    private Button? _pinVisibilityButton;
    private Label? _pinExpiryLabel;
    private Label? _attemptsLabel;
    private Label? _copyIdFeedback;
    private Label? _copyPinFeedback;
    private Image? _qrCodeImage;

    // UI element references — connection status panel
    private Label? _connectedClientsLabel;
    private Label? _fpsLabel;
    private Label? _bandwidthLabel;
    private Label? _latencyLabel;
    private Label? _qualityBadge;
    private StackLayout? _sessionListLayout;
    private Label? _noSessionsLabel;

    // UI element references — partner connection panel
    private Entry? _partnerIdEntry;
    private Entry? _partnerPinEntry;
    private Button? _connectButton;
    private Label? _partnerStatusLabel;
    private Picker? _discoveredHostsPicker;

    // UI element references — session toolbar
    private Border? _sessionToolbar;
    private Button? _recordButton;
    private Button? _chatButton;

    public MainPage(
        ILogger<MainPage> logger,
        RemoteDesktopHost host,
        IPairingService pairing,
        ICommunicationService communication,
        INetworkDiscovery networkDiscovery,
        IInputHandler inputHandler,
        IPerformanceMonitor perfMonitor,
        ISessionManager sessionManager,
        RemoteDesktopClient client,
        WindowsSystemTrayService trayService,
        IAppSettingsService appSettings,
        Func<SettingsPage> settingsPageFactory,
        IFileTransferService fileTransfer,
        ISessionRecorder sessionRecorder,
        IScreenCapture screenCapture,
        IMessagingService messaging,
        Func<ChatPage> chatPageFactory,
        Func<RemoteViewerPage> viewerPageFactory)
    {
        _logger = logger;
        _host = host;
        _pairing = pairing;
        _communication = communication;
        _networkDiscovery = networkDiscovery;
        _inputHandler = inputHandler;
        _perfMonitor = perfMonitor;
        _sessionManager = sessionManager;
        _client = client;
        _trayService = trayService;
        _appSettings = appSettings;
        _settingsPageFactory = settingsPageFactory;
        _fileTransfer = fileTransfer;
        _sessionRecorder = sessionRecorder;
        _screenCapture = screenCapture;
        _messaging = messaging;
        _chatPageFactory = chatPageFactory;
        _viewerPageFactory = viewerPageFactory;

        _deviceNumericId = GenerateNumericId(Environment.MachineName);

        Title = "RemoteLink Desktop";
        BackgroundColor = ThemeColors.PageBackground;

        // Load settings asynchronously; UI is built immediately with defaults
        _ = Task.Run(async () => await _appSettings.LoadAsync());

        Content = BuildLayout();

        // Host-side events
        _communication.ConnectionStateChanged += OnConnectionStateChanged;
        _communication.PairingRequestReceived += OnPairingRequestReceivedUI;

        // Client-side events
        _client.DeviceDiscovered += OnRemoteHostDiscovered;
        _client.DeviceLost += OnRemoteHostLost;
        _client.ConnectionStateChanged += OnClientConnectionStateChanged;
        _client.PairingFailed += OnClientPairingFailed;

        // Chat badge updates
        _messaging.MessageReceived += OnChatMessageReceived;

        // Theme change — rebuild layout
        ThemeColors.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            BackgroundColor = ThemeColors.PageBackground;
            Content = BuildLayout();
        });
    }

    private bool _autoStartChecked;

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // When returning from the viewer page (after disconnect), reset connect UI
        if (!_client.IsConnected && _isConnecting)
        {
            _isConnecting = false;
            SetPartnerStatus("Disconnected", Colors.Gray);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_connectButton != null)
                {
                    _connectButton.Text = "Connect";
                    _connectButton.BackgroundColor = ThemeColors.Accent;
                }
            });
        }

        // Auto-start host on first appearance if setting is enabled
        if (!_autoStartChecked)
        {
            _autoStartChecked = true;
            try
            {
                await _appSettings.LoadAsync();
                if (_appSettings.Current.Startup.StartHostAutomatically && !_isRunning)
                {
                    _logger.LogInformation("Auto-starting host (StartHostAutomatically setting)");
                    await StartHostAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to auto-start host");
            }
        }
    }

    /// <summary>
    /// Generates a stable 9-digit numeric ID from the machine name,
    /// similar to TeamViewer's device IDs.
    /// </summary>
    private static string GenerateNumericId(string machineName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(machineName + "RemoteLink"));
        long value = Math.Abs(BitConverter.ToInt64(hash, 0));
        long id = (value % 900_000_000) + 100_000_000;
        var digits = id.ToString();
        return $"{digits[..3]} {digits[3..6]} {digits[6..]}";
    }

    // ── Layout ─────────────────────────────────────────────────────────

    private View BuildLayout()
    {
        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),   // Header
                new RowDefinition(GridLength.Auto),   // Two-column: Allow Remote Control + Control Remote Computer
                new RowDefinition(GridLength.Star),   // Connection status area
                new RowDefinition(GridLength.Auto),   // Start/Stop button
                new RowDefinition(GridLength.Auto),   // Status bar
            },
            Padding = new Thickness(0),
            RowSpacing = 0
        };

        root.Add(BuildHeader(), 0, 0);
        root.Add(BuildDashboardPanels(), 0, 1);
        root.Add(BuildConnectionPanel(), 0, 2);
        root.Add(BuildControlPanel(), 0, 3);
        root.Add(BuildStatusBar(), 0, 4);

        return root;
    }

    private View BuildHeader()
    {
        var settingsButton = new Button
        {
            Text = "⚙",
            FontSize = 18,
            BackgroundColor = Colors.Transparent,
            TextColor = ThemeColors.AccentText,
            CornerRadius = 4,
            Padding = new Thickness(8, 0),
            HeightRequest = 36,
            WidthRequest = 40,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
        };
        settingsButton.Clicked += OnSettingsClicked;

        return new Grid
        {
            BackgroundColor = ThemeColors.HeaderBackground,
            Padding = new Thickness(20, 16),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
            Children =
            {
                new Label
                {
                    Text = "RemoteLink",
                    FontSize = 24,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    VerticalOptions = LayoutOptions.Center
                },
                CreateGridChild(new Label
                {
                    Text = "Desktop Host",
                    FontSize = 14,
                    TextColor = ThemeColors.AccentText,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.End
                }, column: 1),
                CreateGridChild(settingsButton, column: 2)
            }
        };
    }

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        var page = _settingsPageFactory();
        await Navigation.PushAsync(page);
    }

    /// <summary>
    /// Builds the two side-by-side dashboard panels:
    /// Left: "Allow Remote Control" (host ID + PIN)
    /// Right: "Control Remote Computer" (partner ID + PIN entry)
    /// </summary>
    private View BuildDashboardPanels()
    {
        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 0,
            Children =
            {
                BuildRemoteControlPanel(),
                CreateGridChild(BuildPartnerConnectionPanel(), column: 1)
            }
        };
    }

    /// <summary>
    /// Left panel — "Allow Remote Control": device ID + PIN display.
    /// </summary>
    private View BuildRemoteControlPanel()
    {
        // ── Your ID row ──
        _deviceIdLabel = new Label
        {
            Text = _deviceNumericId,
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.TextPrimary,
            HorizontalOptions = LayoutOptions.Center,
            FontFamily = "OpenSansRegular"
        };

        _copyIdFeedback = new Label
        {
            Text = "",
            FontSize = 10,
            TextColor = ThemeColors.Success,
            HorizontalOptions = LayoutOptions.Center,
            IsVisible = false
        };

        var copyIdButton = new Button
        {
            Text = "Copy",
            FontSize = 10,
            BackgroundColor = ThemeColors.SecondaryButtonBackground,
            TextColor = ThemeColors.Accent,
            CornerRadius = 4,
            Padding = new Thickness(8, 2),
            HeightRequest = 26,
            HorizontalOptions = LayoutOptions.Center
        };
        copyIdButton.Clicked += OnCopyIdClicked;

        // ── PIN row ──
        _pinLabel = new Label
        {
            Text = FormatPinDisplay(),
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.Accent,
            HorizontalOptions = LayoutOptions.Center,
            CharacterSpacing = 4,
            FontFamily = "OpenSansRegular"
        };

        _pinVisibilityButton = new Button
        {
            Text = "Show",
            FontSize = 10,
            BackgroundColor = ThemeColors.SecondaryButtonBackground,
            TextColor = ThemeColors.Accent,
            CornerRadius = 4,
            Padding = new Thickness(8, 2),
            HeightRequest = 26,
            HorizontalOptions = LayoutOptions.Center
        };
        _pinVisibilityButton.Clicked += OnTogglePinVisibility;

        _copyPinFeedback = new Label
        {
            Text = "",
            FontSize = 10,
            TextColor = ThemeColors.Success,
            HorizontalOptions = LayoutOptions.Center,
            IsVisible = false
        };

        var copyPinButton = new Button
        {
            Text = "Copy",
            FontSize = 10,
            BackgroundColor = ThemeColors.SecondaryButtonBackground,
            TextColor = ThemeColors.Accent,
            CornerRadius = 4,
            Padding = new Thickness(8, 2),
            HeightRequest = 26,
            HorizontalOptions = LayoutOptions.Center
        };
        copyPinButton.Clicked += OnCopyPinClicked;

        var pinButtonRow = new HorizontalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Center,
            Children = { _pinVisibilityButton, copyPinButton }
        };

        // ── PIN metadata row ──
        _pinExpiryLabel = new Label
        {
            Text = "",
            FontSize = 10,
            TextColor = ThemeColors.MetricInactive,
            HorizontalOptions = LayoutOptions.Center
        };

        _attemptsLabel = new Label
        {
            Text = "",
            FontSize = 10,
            TextColor = ThemeColors.MetricInactive,
            HorizontalOptions = LayoutOptions.Center
        };

        var pinMetaRow = new HorizontalStackLayout
        {
            Spacing = 12,
            HorizontalOptions = LayoutOptions.Center,
            Children = { _pinExpiryLabel, _attemptsLabel }
        };

        // ── Refresh PIN button ──
        var refreshButton = new Button
        {
            Text = "Refresh PIN",
            FontSize = 11,
            BackgroundColor = ThemeColors.SecondaryButtonBackground,
            TextColor = ThemeColors.Accent,
            CornerRadius = 4,
            Padding = new Thickness(10, 2),
            HeightRequest = 28,
            HorizontalOptions = LayoutOptions.Center
        };
        refreshButton.Clicked += OnRefreshPinClicked;

        // ── QR code image ──
        _qrCodeImage = new Image
        {
            WidthRequest = 140,
            HeightRequest = 140,
            HorizontalOptions = LayoutOptions.Center,
            Aspect = Aspect.AspectFit,
            IsVisible = false,
            Margin = new Thickness(0, 6, 0, 0)
        };

        var divider = new BoxView
        {
            Color = ThemeColors.Divider,
            HeightRequest = 1,
            Margin = new Thickness(0, 6, 0, 6)
        };

        return new Border
        {
            Margin = new Thickness(12, 12, 6, 4),
            Padding = new Thickness(14, 12),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            BackgroundColor = ThemeColors.CardBackground,
            Stroke = ThemeColors.CardBorder,
            StrokeThickness = 1,
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(ThemeColors.CardShadow),
                Offset = new Point(0, 2),
                Radius = 6
            },
            Content = new StackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label
                    {
                        Text = "Allow Remote Control",
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = ThemeColors.Accent,
                        HorizontalOptions = LayoutOptions.Center,
                        Margin = new Thickness(0, 0, 0, 6)
                    },
                    new Label { Text = "Your ID", FontSize = 11, TextColor = ThemeColors.TextSecondary, HorizontalOptions = LayoutOptions.Center },
                    _deviceIdLabel,
                    new HorizontalStackLayout
                    {
                        Spacing = 4,
                        HorizontalOptions = LayoutOptions.Center,
                        Children = { copyIdButton, _copyIdFeedback }
                    },
                    divider,
                    new Label { Text = "Password", FontSize = 11, TextColor = ThemeColors.TextSecondary, HorizontalOptions = LayoutOptions.Center },
                    _pinLabel,
                    pinButtonRow,
                    _copyPinFeedback,
                    pinMetaRow,
                    refreshButton,
                }
            }
        };
    }

    /// <summary>
    /// Right panel — "Control Remote Computer": partner ID + PIN entry + connect button.
    /// Enables desktop-to-desktop connections.
    /// </summary>
    private View BuildPartnerConnectionPanel()
    {
        // ── Discovered hosts picker ──
        _discoveredHostsPicker = new Picker
        {
            Title = "Select discovered host...",
            FontSize = 13,
            HorizontalOptions = LayoutOptions.Fill,
            TextColor = ThemeColors.TextPrimary,
            TitleColor = ThemeColors.EntryPlaceholder,
        };
        _discoveredHostsPicker.SelectedIndexChanged += OnDiscoveredHostSelected;

        // ── Partner ID entry ──
        _partnerIdEntry = new Entry
        {
            Placeholder = "Partner ID or IP address",
            FontSize = 14,
            Keyboard = Keyboard.Text,
            HorizontalOptions = LayoutOptions.Fill,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            TextColor = ThemeColors.TextPrimary,
            PlaceholderColor = ThemeColors.EntryPlaceholder,
        };

        // ── Partner PIN entry ──
        _partnerPinEntry = new Entry
        {
            Placeholder = "PIN",
            FontSize = 14,
            Keyboard = Keyboard.Numeric,
            MaxLength = 6,
            IsPassword = true,
            HorizontalOptions = LayoutOptions.Fill,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            TextColor = ThemeColors.TextPrimary,
            PlaceholderColor = ThemeColors.EntryPlaceholder,
        };

        // ── Connect button ──
        _connectButton = new Button
        {
            Text = "Connect",
            FontSize = 14,
            BackgroundColor = ThemeColors.Accent,
            TextColor = Colors.White,
            CornerRadius = 6,
            HeightRequest = 40,
            HorizontalOptions = LayoutOptions.Fill,
        };
        _connectButton.Clicked += OnConnectToPartnerClicked;

        // ── Status label ──
        _partnerStatusLabel = new Label
        {
            Text = "",
            FontSize = 11,
            TextColor = ThemeColors.MetricInactive,
            HorizontalOptions = LayoutOptions.Center
        };

        // ── "or" separator ──
        var orSeparator = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            Margin = new Thickness(0, 2),
            Children =
            {
                new BoxView { Color = ThemeColors.SeparatorLight, HeightRequest = 1, VerticalOptions = LayoutOptions.Center },
                CreateGridChild(new Label
                {
                    Text = "or enter manually",
                    FontSize = 10,
                    TextColor = ThemeColors.PartnerSeparatorText,
                    Margin = new Thickness(8, 0),
                    VerticalOptions = LayoutOptions.Center,
                }, column: 1),
                CreateGridChild(new BoxView { Color = ThemeColors.SeparatorLight, HeightRequest = 1, VerticalOptions = LayoutOptions.Center }, column: 2),
            }
        };

        return new Border
        {
            Margin = new Thickness(6, 12, 12, 4),
            Padding = new Thickness(14, 12),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            BackgroundColor = ThemeColors.CardBackground,
            Stroke = ThemeColors.CardBorder,
            StrokeThickness = 1,
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(ThemeColors.CardShadow),
                Offset = new Point(0, 2),
                Radius = 6
            },
            Content = new StackLayout
            {
                Spacing = 6,
                Children =
                {
                    new Label
                    {
                        Text = "Control Remote Computer",
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = ThemeColors.Accent,
                        HorizontalOptions = LayoutOptions.Center,
                        Margin = new Thickness(0, 0, 0, 4)
                    },
                    new Label { Text = "Discovered Hosts", FontSize = 11, TextColor = ThemeColors.TextSecondary },
                    _discoveredHostsPicker,
                    orSeparator,
                    new Label { Text = "Partner ID", FontSize = 11, TextColor = ThemeColors.TextSecondary },
                    _partnerIdEntry,
                    new Label { Text = "Password", FontSize = 11, TextColor = ThemeColors.TextSecondary },
                    _partnerPinEntry,
                    _connectButton,
                    _partnerStatusLabel,
                }
            }
        };
    }

    private View BuildConnectionPanel()
    {
        // ── Status indicator + label row ──
        _statusIndicator = new BoxView
        {
            Color = Colors.Gray,
            WidthRequest = 12,
            HeightRequest = 12,
            CornerRadius = 6,
            VerticalOptions = LayoutOptions.Center
        };

        _connectionLabel = new Label
        {
            Text = _connectionInfo,
            FontSize = 14,
            TextColor = ThemeColors.ConnectionInfoText,
            VerticalOptions = LayoutOptions.Center
        };

        _connectedClientsLabel = new Label
        {
            Text = "0 clients",
            FontSize = 11,
            TextColor = ThemeColors.MetricInactive,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
        };

        var statusRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 8,
            Children =
            {
                _statusIndicator,
                CreateGridChild(_connectionLabel, column: 1),
                CreateGridChild(_connectedClientsLabel, column: 2),
            }
        };

        // ── Metrics row: FPS | Bandwidth | Latency | Quality ──
        _fpsLabel = new Label
        {
            Text = "-- fps",
            FontSize = 12,
            TextColor = ThemeColors.MetricInactive,
            HorizontalTextAlignment = TextAlignment.Center
        };

        _bandwidthLabel = new Label
        {
            Text = "-- KB/s",
            FontSize = 12,
            TextColor = ThemeColors.MetricInactive,
            HorizontalTextAlignment = TextAlignment.Center
        };

        _latencyLabel = new Label
        {
            Text = "-- ms",
            FontSize = 12,
            TextColor = ThemeColors.MetricInactive,
            HorizontalTextAlignment = TextAlignment.Center
        };

        _qualityBadge = new Label
        {
            Text = "--",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = ThemeColors.QualityInactive,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            Padding = new Thickness(8, 2),
            HorizontalOptions = LayoutOptions.Center
        };

        var metricsRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 6,
            Children =
            {
                BuildMetricCell("FPS", _fpsLabel),
                CreateGridChild(BuildMetricCell("Bandwidth", _bandwidthLabel), column: 1),
                CreateGridChild(BuildMetricCell("Latency", _latencyLabel), column: 2),
                CreateGridChild(BuildMetricCell("Quality", _qualityBadge), column: 3),
            }
        };

        // ── Active sessions list ──
        _noSessionsLabel = new Label
        {
            Text = "No active sessions",
            FontSize = 12,
            TextColor = ThemeColors.TextMuted,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 4)
        };

        _sessionListLayout = new StackLayout
        {
            Spacing = 4,
            Children = { _noSessionsLabel }
        };

        var divider = new BoxView
        {
            Color = ThemeColors.SeparatorLight,
            HeightRequest = 1,
            Margin = new Thickness(0, 4, 0, 2)
        };

        return new Border
        {
            Margin = new Thickness(16, 4, 16, 4),
            Padding = new Thickness(16, 12),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            BackgroundColor = ThemeColors.CardBackground,
            Stroke = ThemeColors.ConnectionPanelBorder,
            StrokeThickness = 1,
            VerticalOptions = LayoutOptions.Fill,
            Content = new StackLayout
            {
                Spacing = 8,
                Children =
                {
                    new Label
                    {
                        Text = "Connection Status",
                        FontSize = 12,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = ThemeColors.TextSecondary,
                    },
                    statusRow,
                    metricsRow,
                    divider,
                    new Label
                    {
                        Text = "Active Sessions",
                        FontSize = 11,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = ThemeColors.TextSecondary,
                    },
                    _sessionListLayout,
                    BuildSessionToolbar(),
                }
            }
        };
    }

    private static View BuildMetricCell(string header, View valueView)
    {
        return new StackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label
                {
                    Text = header,
                    FontSize = 9,
                    TextColor = ThemeColors.TextMuted,
                    HorizontalTextAlignment = TextAlignment.Center
                },
                valueView,
            }
        };
    }

    private View BuildControlPanel()
    {
        _startStopButton = new Button
        {
            Text = "Start Host",
            FontSize = 16,
            BackgroundColor = ThemeColors.Accent,
            TextColor = Colors.White,
            CornerRadius = 8,
            HeightRequest = 48,
            Margin = new Thickness(16, 8, 16, 8)
        };
        _startStopButton.Clicked += OnStartStopClicked;

        return _startStopButton;
    }

    private View BuildStatusBar()
    {
        _statusLabel = new Label
        {
            Text = _statusText,
            FontSize = 12,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(12, 0)
        };

        return new Grid
        {
            BackgroundColor = ThemeColors.StatusBarBackground,
            Padding = new Thickness(8, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            Children =
            {
                _statusLabel,
                CreateGridChild(new Label
                {
                    Text = "v1.0",
                    FontSize = 11,
                    TextColor = ThemeColors.MetricInactive,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.End
                }, column: 1)
            }
        };
    }

    // ── PIN display helpers ────────────────────────────────────────────

    private string FormatPinDisplay()
    {
        if (_currentPin == "------")
            return "--- ---";

        if (!_pinVisible)
            return "*** ***";

        return _currentPin.Length == 6
            ? $"{_currentPin[..3]} {_currentPin[3..]}"
            : _currentPin;
    }

    private void RefreshPinDisplay()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_pinLabel != null)
                _pinLabel.Text = FormatPinDisplay();
            if (_pinVisibilityButton != null)
                _pinVisibilityButton.Text = _pinVisible ? "Hide" : "Show";
        });
    }

    private void UpdatePinMetadata()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_isRunning || _currentPin == "------")
            {
                if (_pinExpiryLabel != null) { _pinExpiryLabel.Text = ""; _pinExpiryLabel.IsVisible = false; }
                if (_attemptsLabel != null) { _attemptsLabel.Text = ""; _attemptsLabel.IsVisible = false; }
                return;
            }

            if (_pinExpiryLabel != null)
            {
                _pinExpiryLabel.IsVisible = true;
                if (_pairing.IsPinExpired)
                {
                    _pinExpiryLabel.Text = "PIN expired";
                    _pinExpiryLabel.TextColor = ThemeColors.Danger;
                }
                else
                {
                    _pinExpiryLabel.Text = "PIN active";
                    _pinExpiryLabel.TextColor = ThemeColors.Success;
                }
            }

            if (_attemptsLabel != null)
            {
                _attemptsLabel.IsVisible = true;
                if (_pairing.IsLockedOut)
                {
                    _attemptsLabel.Text = "Locked out";
                    _attemptsLabel.TextColor = ThemeColors.Danger;
                }
                else
                {
                    int remaining = _pairing.AttemptsRemaining;
                    _attemptsLabel.Text = $"{remaining} attempts left";
                    _attemptsLabel.TextColor = remaining <= 2
                        ? ThemeColors.Warning
                        : ThemeColors.MetricInactive;
                }
            }
        });
    }

    private void StartPinExpiryTimer()
    {
        StopPinExpiryTimer();
        _pinExpiryTimer = Dispatcher.CreateTimer();
        _pinExpiryTimer.Interval = TimeSpan.FromSeconds(5);
        _pinExpiryTimer.Tick += (_, _) => UpdatePinMetadata();
        _pinExpiryTimer.Start();
        UpdatePinMetadata();
    }

    private void StopPinExpiryTimer()
    {
        _pinExpiryTimer?.Stop();
        _pinExpiryTimer = null;
    }

    // ── Metrics timer ──────────────────────────────────────────────────

    private void StartMetricsTimer()
    {
        StopMetricsTimer();
        _metricsTimer = Dispatcher.CreateTimer();
        _metricsTimer.Interval = TimeSpan.FromSeconds(2);
        _metricsTimer.Tick += (_, _) => RefreshConnectionMetrics();
        _metricsTimer.Start();
    }

    private void StopMetricsTimer()
    {
        _metricsTimer?.Stop();
        _metricsTimer = null;
    }

    private void RefreshConnectionMetrics()
    {
        var fps = _perfMonitor.GetCurrentFps();
        var bandwidth = _perfMonitor.GetCurrentBandwidth();
        var latency = _perfMonitor.GetAverageLatency();
        var rating = ConnectionQuality.CalculateRating(fps, latency, bandwidth);

        var bandwidthText = bandwidth < 1024
            ? $"{bandwidth} B/s"
            : bandwidth < 1024 * 1024
                ? $"{bandwidth / 1024.0:F1} KB/s"
                : $"{bandwidth / (1024.0 * 1024.0):F1} MB/s";

        var (ratingText, ratingColor) = rating switch
        {
            QualityRating.Excellent => ("Excellent", ThemeColors.Success),
            QualityRating.Good => ("Good", ThemeColors.QualityGood),
            QualityRating.Fair => ("Fair", ThemeColors.Warning),
            QualityRating.Poor => ("Poor", ThemeColors.Danger),
            _ => ("--", ThemeColors.QualityInactive)
        };

        var sessions = _sessionManager.GetAllSessions()
            .Where(s => s.Status == SessionStatus.Connected)
            .ToList();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            bool hasActiveConnections = _activeConnectionCount > 0;

            if (_fpsLabel != null)
            {
                _fpsLabel.Text = hasActiveConnections ? $"{fps:F1} fps" : "-- fps";
                _fpsLabel.TextColor = hasActiveConnections ? ThemeColors.TextPrimary : ThemeColors.MetricInactive;
            }

            if (_bandwidthLabel != null)
            {
                _bandwidthLabel.Text = hasActiveConnections ? bandwidthText : "-- KB/s";
                _bandwidthLabel.TextColor = hasActiveConnections ? ThemeColors.TextPrimary : ThemeColors.MetricInactive;
            }

            if (_latencyLabel != null)
            {
                _latencyLabel.Text = hasActiveConnections ? $"{latency} ms" : "-- ms";
                _latencyLabel.TextColor = hasActiveConnections ? ThemeColors.TextPrimary : ThemeColors.MetricInactive;
            }

            if (_qualityBadge != null)
            {
                _qualityBadge.Text = hasActiveConnections ? ratingText : "--";
                _qualityBadge.BackgroundColor = hasActiveConnections ? ratingColor : ThemeColors.QualityInactive;
            }

            if (_connectedClientsLabel != null)
            {
                _connectedClientsLabel.Text = _activeConnectionCount == 1
                    ? "1 client"
                    : $"{_activeConnectionCount} clients";
            }

            RefreshSessionList(sessions);
        });
    }

    private void RefreshSessionList(List<RemoteSession> activeSessions)
    {
        if (_sessionListLayout == null) return;

        _sessionListLayout.Children.Clear();

        if (activeSessions.Count == 0)
        {
            _noSessionsLabel ??= new Label
            {
                Text = "No active sessions",
                FontSize = 12,
                TextColor = ThemeColors.TextMuted,
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 4)
            };
            _sessionListLayout.Children.Add(_noSessionsLabel);
            return;
        }

        foreach (var session in activeSessions)
        {
            var duration = session.Duration;
            var durationText = duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours}h {duration.Minutes:D2}m {duration.Seconds:D2}s"
                : $"{duration.Minutes:D2}m {duration.Seconds:D2}s";

            var sessionRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
                ColumnSpacing = 8,
                Padding = new Thickness(4, 3),
                BackgroundColor = ThemeColors.SessionRowBackground,
                Children =
                {
                    new BoxView
                    {
                        Color = ThemeColors.Success,
                        WidthRequest = 8,
                        HeightRequest = 8,
                        CornerRadius = 4,
                        VerticalOptions = LayoutOptions.Center
                    },
                    CreateGridChild(new Label
                    {
                        Text = session.ClientDeviceName,
                        FontSize = 12,
                        TextColor = ThemeColors.TextPrimary,
                        VerticalOptions = LayoutOptions.Center,
                        LineBreakMode = LineBreakMode.TailTruncation
                    }, column: 1),
                    CreateGridChild(new Label
                    {
                        Text = durationText,
                        FontSize = 11,
                        TextColor = ThemeColors.TextSecondary,
                        VerticalOptions = LayoutOptions.Center
                    }, column: 2),
                }
            };

            _sessionListLayout.Children.Add(sessionRow);
        }
    }

    // ── Host-side event handlers ───────────────────────────────────────

    private async void OnStartStopClicked(object? sender, EventArgs e)
    {
        if (_isRunning)
            await StopHostAsync();
        else
            await StartHostAsync();
    }

    private async Task StartHostAsync()
    {
        try
        {
            _isRunning = true;
            UpdateStatusBar("Starting...", ThemeColors.Warning);

            _currentPin = _pairing.GeneratePin();
            _pinVisible = false;
            RefreshPinDisplay();
            StartPinExpiryTimer();

            _hostCts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try
                {
                    await _host.StartAsync(_hostCts.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Host service error");
                    MainThread.BeginInvokeOnMainThread(() => UpdateStatusBar("Error: " + ex.Message, Colors.Red));
                }
            });

            // Also start client-side discovery so we can find other hosts
            _ = Task.Run(async () =>
            {
                try { await _client.StartAsync(); }
                catch (Exception ex) { _logger.LogWarning(ex, "Client discovery start failed"); }
            });

            UpdateStatusBar("Running — Listening for connections", ThemeColors.Success);
            _trayService.UpdateStatus("Running", 0);
            StartMetricsTimer();
            RefreshConnectionMetrics();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_startStopButton != null)
                {
                    _startStopButton.Text = "Stop Host";
                    _startStopButton.BackgroundColor = ThemeColors.Danger;
                }
            });

            _logger.LogInformation("Desktop host started from UI");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start host");
            _isRunning = false;
            UpdateStatusBar("Failed to start: " + ex.Message, Colors.Red);
            await DisplayAlertAsync("Error", $"Failed to start host: {ex.Message}", "OK");
        }
    }

    private async Task StopHostAsync()
    {
        try
        {
            UpdateStatusBar("Stopping...", ThemeColors.Warning);

            _hostCts?.Cancel();
            await _host.StopAsync(CancellationToken.None);
            await _client.StopAsync();

            _isRunning = false;
            _currentPin = "------";
            _pinVisible = false;
            RefreshPinDisplay();
            StopPinExpiryTimer();
            UpdatePinMetadata();

            // Clear discovered hosts
            _discoveredHosts.Clear();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _discoveredHostsPicker?.Items.Clear();
            });

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_startStopButton != null)
                {
                    _startStopButton.Text = "Start Host";
                    _startStopButton.BackgroundColor = ThemeColors.Accent;
                }
            });

            _activeConnectionCount = 0;
            StopMetricsTimer();
            RefreshConnectionMetrics();
            UpdateStatusBar("Stopped", Colors.Gray);
            _trayService.UpdateStatus("Stopped", 0);
            _logger.LogInformation("Desktop host stopped from UI");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop host");
            UpdateStatusBar("Error stopping: " + ex.Message, Colors.Red);
        }
    }

    private void OnRefreshPinClicked(object? sender, EventArgs e)
    {
        if (!_isRunning) return;

        _currentPin = _pairing.GeneratePin();
        _pinVisible = false;
        RefreshPinDisplay();
        UpdatePinMetadata();

        _logger.LogInformation("PIN refreshed from UI");
    }

    private void OnTogglePinVisibility(object? sender, EventArgs e)
    {
        _pinVisible = !_pinVisible;
        RefreshPinDisplay();
    }

    private async void OnCopyIdClicked(object? sender, EventArgs e)
    {
        var rawId = _deviceNumericId.Replace(" ", "");
        await Clipboard.Default.SetTextAsync(rawId);
        ShowCopyFeedback(_copyIdFeedback);
    }

    private async void OnCopyPinClicked(object? sender, EventArgs e)
    {
        if (_currentPin == "------") return;
        await Clipboard.Default.SetTextAsync(_currentPin);
        ShowCopyFeedback(_copyPinFeedback);
    }

    private void ShowCopyFeedback(Label? feedbackLabel)
    {
        if (feedbackLabel == null) return;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            feedbackLabel.Text = "Copied!";
            feedbackLabel.IsVisible = true;
            await Task.Delay(1500);
            feedbackLabel.IsVisible = false;
            feedbackLabel.Text = "";
        });
    }

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        if (connected)
            _activeConnectionCount++;
        else
            _activeConnectionCount = Math.Max(0, _activeConnectionCount - 1);

        _trayService.UpdateStatus(
            _isRunning ? "Running" : "Stopped",
            _activeConnectionCount);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (connected)
            {
                _connectionInfo = "Client connected — awaiting PIN pairing";
                if (_statusIndicator != null) _statusIndicator.Color = ThemeColors.Warning;
                if (_connectionLabel != null) _connectionLabel.Text = _connectionInfo;
            }
            else
            {
                _connectionInfo = "No active connections";
                if (_statusIndicator != null) _statusIndicator.Color = _isRunning ? ThemeColors.Success : Colors.Gray;
                if (_connectionLabel != null) _connectionLabel.Text = _connectionInfo;
                UpdatePinMetadata();
            }
            UpdateToolbarVisibility();
        });
    }

    private void OnPairingRequestReceivedUI(object? sender, PairingRequest request)
    {
        UpdatePinMetadata();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_connectionLabel != null)
                _connectionLabel.Text = $"Pairing attempt from {request.ClientDeviceName}...";
        });
    }

    // ── Partner connection (client-side) event handlers ────────────────

    private void OnRemoteHostDiscovered(object? sender, DeviceInfo host)
    {
        // Don't show ourselves in the discovered list
        if (host.DeviceName == Environment.MachineName) return;

        lock (_discoveredHosts)
        {
            if (_discoveredHosts.Any(h => h.DeviceId == host.DeviceId))
                return;
            _discoveredHosts.Add(host);
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var displayName = $"{host.DeviceName} ({host.IPAddress}:{host.Port})";
            _discoveredHostsPicker?.Items.Add(displayName);
        });

        _logger.LogInformation("Discovered remote host: {Name} at {IP}:{Port}",
            host.DeviceName, host.IPAddress, host.Port);
    }

    private void OnRemoteHostLost(object? sender, DeviceInfo host)
    {
        int removedIndex;
        lock (_discoveredHosts)
        {
            removedIndex = _discoveredHosts.FindIndex(h => h.DeviceId == host.DeviceId);
            if (removedIndex >= 0)
                _discoveredHosts.RemoveAt(removedIndex);
        }

        if (removedIndex >= 0)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_discoveredHostsPicker != null && removedIndex < _discoveredHostsPicker.Items.Count)
                    _discoveredHostsPicker.Items.RemoveAt(removedIndex);
            });
        }
    }

    private void OnDiscoveredHostSelected(object? sender, EventArgs e)
    {
        if (_discoveredHostsPicker == null) return;
        int idx = _discoveredHostsPicker.SelectedIndex;
        if (idx < 0) return;

        DeviceInfo? selected;
        lock (_discoveredHosts)
        {
            selected = idx < _discoveredHosts.Count ? _discoveredHosts[idx] : null;
        }

        if (selected != null && _partnerIdEntry != null)
        {
            // Fill the Partner ID field with IP:Port for the selected host
            _partnerIdEntry.Text = $"{selected.IPAddress}:{selected.Port}";
        }
    }

    private async void OnConnectToPartnerClicked(object? sender, EventArgs e)
    {
        if (_isConnecting)
        {
            // Disconnect
            await DisconnectFromPartnerAsync();
            return;
        }

        var partnerId = _partnerIdEntry?.Text?.Trim();
        var pin = _partnerPinEntry?.Text?.Trim();

        if (string.IsNullOrEmpty(partnerId))
        {
            SetPartnerStatus("Enter a Partner ID or select a discovered host", ThemeColors.Danger);
            return;
        }

        if (string.IsNullOrEmpty(pin) || pin.Length != 6)
        {
            SetPartnerStatus("Enter the 6-digit PIN from the remote host", ThemeColors.Danger);
            return;
        }

        // Resolve the partner to a DeviceInfo
        var targetDevice = ResolvePartner(partnerId);
        if (targetDevice == null)
        {
            SetPartnerStatus("Cannot resolve partner — use IP:Port format", ThemeColors.Danger);
            return;
        }

        _isConnecting = true;
        SetPartnerStatus($"Connecting to {targetDevice.IPAddress}:{targetDevice.Port}...", ThemeColors.Warning);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_connectButton != null)
            {
                _connectButton.Text = "Cancel";
                _connectButton.BackgroundColor = ThemeColors.Danger;
            }
        });

        var success = await _client.ConnectToHostAsync(targetDevice, pin);

        if (success)
        {
            SetPartnerStatus($"Connected to {targetDevice.DeviceName}", ThemeColors.Success);
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (_connectButton != null)
                {
                    _connectButton.Text = "Disconnect";
                    _connectButton.BackgroundColor = ThemeColors.Danger;
                }

                // Open the remote viewer page
                var viewerPage = _viewerPageFactory();
                await Navigation.PushAsync(viewerPage);
            });
        }
        else
        {
            _isConnecting = false;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_connectButton != null)
                {
                    _connectButton.Text = "Connect";
                    _connectButton.BackgroundColor = ThemeColors.Accent;
                }
            });
            // Status is set by PairingFailed event handler
        }
    }

    private async Task DisconnectFromPartnerAsync()
    {
        await _client.DisconnectAsync();
        _isConnecting = false;

        SetPartnerStatus("Disconnected", Colors.Gray);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_connectButton != null)
            {
                _connectButton.Text = "Connect";
                _connectButton.BackgroundColor = ThemeColors.Accent;
            }
        });
    }

    private void OnClientConnectionStateChanged(object? sender, ClientConnectionState state)
    {
        var text = state switch
        {
            ClientConnectionState.Connecting => "Connecting...",
            ClientConnectionState.Authenticating => "Authenticating...",
            ClientConnectionState.Connected => "Connected",
            ClientConnectionState.Disconnected => _isConnecting ? "Connection lost" : "",
            _ => ""
        };

        var color = state switch
        {
            ClientConnectionState.Connected => ThemeColors.Success,
            ClientConnectionState.Disconnected => Colors.Gray,
            _ => ThemeColors.Warning
        };

        if (!string.IsNullOrEmpty(text))
            SetPartnerStatus(text, color);

        if (state == ClientConnectionState.Disconnected && _isConnecting)
        {
            _isConnecting = false;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_connectButton != null)
                {
                    _connectButton.Text = "Connect";
                    _connectButton.BackgroundColor = ThemeColors.Accent;
                }
            });
        }
    }

    private void OnClientPairingFailed(object? sender, string reason)
    {
        SetPartnerStatus(reason, ThemeColors.Danger);
    }

    /// <summary>
    /// Resolves a partner ID string to a DeviceInfo for connection.
    /// Supports: IP:Port format, plain IP (default port 12346), or
    /// lookup by numeric ID in discovered hosts.
    /// </summary>
    private DeviceInfo? ResolvePartner(string partnerId)
    {
        // Try to match against discovered hosts by numeric ID
        var strippedId = partnerId.Replace(" ", "");
        lock (_discoveredHosts)
        {
            foreach (var host in _discoveredHosts)
            {
                var hostNumericId = GenerateNumericId(host.DeviceName).Replace(" ", "");
                if (hostNumericId == strippedId)
                    return host;
            }
        }

        // Try IP:Port format
        if (partnerId.Contains(':'))
        {
            var parts = partnerId.Split(':', 2);
            if (parts.Length == 2 && int.TryParse(parts[1], out int port))
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

        // Try plain IP (use default port)
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

    private void SetPartnerStatus(string text, Color color)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_partnerStatusLabel != null)
            {
                _partnerStatusLabel.Text = text;
                _partnerStatusLabel.TextColor = color;
            }
        });
    }

    // ── Session toolbar ────────────────────────────────────────────────

    private View BuildSessionToolbar()
    {
        var filesButton = new Button
        {
            Text = "📁 Files",
            FontSize = 11,
            BackgroundColor = ThemeColors.SecondaryButtonBackground,
            TextColor = ThemeColors.Accent,
            CornerRadius = 5,
            Padding = new Thickness(10, 4),
            HeightRequest = 32,
        };
        filesButton.Clicked += OnFileTransferClicked;

        _chatButton = new Button
        {
            Text = "💬 Chat",
            FontSize = 11,
            BackgroundColor = ThemeColors.SecondaryButtonBackground,
            TextColor = ThemeColors.Accent,
            CornerRadius = 5,
            Padding = new Thickness(10, 4),
            HeightRequest = 32,
        };
        _chatButton.Clicked += OnChatClicked;

        _recordButton = new Button
        {
            Text = "⏺ Record",
            FontSize = 11,
            BackgroundColor = ThemeColors.SecondaryButtonBackground,
            TextColor = ThemeColors.Accent,
            CornerRadius = 5,
            Padding = new Thickness(10, 4),
            HeightRequest = 32,
        };
        _recordButton.Clicked += OnRecordClicked;

        var qualityButton = new Button
        {
            Text = "📊 Quality",
            FontSize = 11,
            BackgroundColor = ThemeColors.SecondaryButtonBackground,
            TextColor = ThemeColors.Accent,
            CornerRadius = 5,
            Padding = new Thickness(10, 4),
            HeightRequest = 32,
        };
        qualityButton.Clicked += OnQualityClicked;

        var monitorButton = new Button
        {
            Text = "🖥 Monitor",
            FontSize = 11,
            BackgroundColor = ThemeColors.SecondaryButtonBackground,
            TextColor = ThemeColors.Accent,
            CornerRadius = 5,
            Padding = new Thickness(10, 4),
            HeightRequest = 32,
        };
        monitorButton.Clicked += OnMonitorClicked;

        var disconnectButton = new Button
        {
            Text = "✕ Disconnect",
            FontSize = 11,
            BackgroundColor = ThemeColors.DisconnectTint,
            TextColor = ThemeColors.Danger,
            CornerRadius = 5,
            Padding = new Thickness(10, 4),
            HeightRequest = 32,
        };
        disconnectButton.Clicked += OnDisconnectAllClicked;

        var buttonRow = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            Content = new HorizontalStackLayout
            {
                Spacing = 6,
                Children = { filesButton, _chatButton, _recordButton, qualityButton, monitorButton, disconnectButton }
            }
        };

        _sessionToolbar = new Border
        {
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(8, 6),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            BackgroundColor = ThemeColors.ToolbarBackground,
            Stroke = ThemeColors.ToolbarBorder,
            StrokeThickness = 1,
            IsVisible = false,
            Content = new StackLayout
            {
                Spacing = 4,
                Children =
                {
                    new Label
                    {
                        Text = "Session Actions",
                        FontSize = 10,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = ThemeColors.TextSecondary,
                    },
                    buttonRow
                }
            }
        };

        return _sessionToolbar;
    }

    private void UpdateToolbarVisibility()
    {
        if (_sessionToolbar != null)
            _sessionToolbar.IsVisible = _activeConnectionCount > 0;
    }

    private void UpdateChatButton()
    {
        if (_chatButton == null) return;
        int unread = _messaging.UnreadCount;
        _chatButton.Text = unread > 0 ? $"💬 Chat ({unread})" : "💬 Chat";
    }

    private void OnChatMessageReceived(object? sender, ChatMessage message)
    {
        MainThread.BeginInvokeOnMainThread(UpdateChatButton);
    }

    private async void OnFileTransferClicked(object? sender, EventArgs e)
    {
        if (_activeConnectionCount == 0)
        {
            await DisplayAlertAsync("No Connection", "No active connections for file transfer.", "OK");
            return;
        }

        try
        {
            var result = await FilePicker.Default.PickAsync();
            if (result == null) return;

            var transferId = await _fileTransfer.InitiateTransferAsync(
                result.FullPath,
                FileTransferDirection.Upload);

            _logger.LogInformation("File transfer initiated: {TransferId} for {File}", transferId, result.FileName);
            await DisplayAlertAsync("File Transfer", $"Sending '{result.FileName}'...", "OK");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File transfer failed");
            await DisplayAlertAsync("Error", $"File transfer failed: {ex.Message}", "OK");
        }
    }

    private async void OnChatClicked(object? sender, EventArgs e)
    {
        var page = _chatPageFactory();
        await Navigation.PushAsync(page);
        MainThread.BeginInvokeOnMainThread(UpdateChatButton);
    }

    private async void OnRecordClicked(object? sender, EventArgs e)
    {
        if (_sessionRecorder.IsRecording)
        {
            await _sessionRecorder.StopRecordingAsync();
            if (_recordButton != null)
            {
                _recordButton.Text = "⏺ Record";
                _recordButton.BackgroundColor = ThemeColors.SecondaryButtonBackground;
                _recordButton.TextColor = ThemeColors.Accent;
            }
            _logger.LogInformation("Session recording stopped from toolbar");
        }
        else
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                $"RemoteLink_session_{timestamp}.mp4");

            var started = await _sessionRecorder.StartRecordingAsync(path);
            if (started)
            {
                if (_recordButton != null)
                {
                    _recordButton.Text = "⏹ Stop Rec";
                    _recordButton.BackgroundColor = ThemeColors.Danger;
                    _recordButton.TextColor = Colors.White;
                }
                _logger.LogInformation("Session recording started: {Path}", path);
            }
            else
            {
                await DisplayAlertAsync("Recording", "Could not start recording. Ensure FFmpeg is installed.", "OK");
            }
        }
    }

    private async void OnQualityClicked(object? sender, EventArgs e)
    {
        var choice = await DisplayActionSheetAsync(
            "Capture Quality",
            "Cancel",
            null,
            "Low (50%)", "Medium (65%)", "High (75%)", "Ultra (85%)");

        var quality = choice switch
        {
            "Low (50%)" => 50,
            "Medium (65%)" => 65,
            "High (75%)" => 75,
            "Ultra (85%)" => 85,
            _ => -1
        };

        if (quality >= 0)
        {
            _screenCapture.SetQuality(quality);
            _logger.LogInformation("Capture quality set to {Quality}%", quality);
        }
    }

    private async void OnMonitorClicked(object? sender, EventArgs e)
    {
        try
        {
            var monitors = await _screenCapture.GetMonitorsAsync();
            if (monitors.Count == 0)
            {
                await DisplayAlertAsync("Monitors", "No monitors found.", "OK");
                return;
            }

            var selectedId = _screenCapture.GetSelectedMonitorId();
            var options = monitors
                .Select(m => $"{m.Name} ({m.Width}×{m.Height}){(m.IsPrimary ? " [Primary]" : "")}{(m.Id == selectedId ? " ✓" : "")}")
                .ToArray();

            var choice = await DisplayActionSheetAsync("Select Monitor", "Cancel", null, options);
            if (string.IsNullOrEmpty(choice) || choice == "Cancel") return;

            int idx = Array.IndexOf(options, choice);
            if (idx >= 0)
            {
                await _screenCapture.SelectMonitorAsync(monitors[idx].Id);
                _logger.LogInformation("Monitor selected: {Monitor}", monitors[idx].Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Monitor selection failed");
            await DisplayAlertAsync("Error", $"Failed to get monitors: {ex.Message}", "OK");
        }
    }

    private async void OnDisconnectAllClicked(object? sender, EventArgs e)
    {
        bool confirm = await DisplayAlertAsync(
            "Disconnect All",
            "Disconnect all active sessions and stop the host?",
            "Disconnect",
            "Cancel");

        if (confirm)
            await StopHostAsync();
    }

    // ── Shared helpers ─────────────────────────────────────────────────

    private void UpdateStatusBar(string status, Color color)
    {
        _statusText = status;
        _statusColor = color;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_statusLabel != null) _statusLabel.Text = _statusText;
            if (_statusIndicator != null && !_statusText.Contains("connection"))
                _statusIndicator.Color = _statusColor;
        });
    }

    private static View CreateGridChild(View view, int column = 0, int row = 0)
    {
        Grid.SetColumn(view, column);
        Grid.SetRow(view, row);
        return view;
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected override void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
