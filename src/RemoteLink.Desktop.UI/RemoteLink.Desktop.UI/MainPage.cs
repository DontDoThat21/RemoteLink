using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
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
    private readonly RemoteDesktopMultiSessionManager _multiSessionManager;
    private readonly Func<ChatPage> _chatPageFactory;
    private readonly Func<SessionWorkspacePage> _sessionWorkspacePageFactory;
    private readonly DeviceInfo _localDevice;
    private readonly ISavedDevicesService _savedDevices;
    private readonly IConnectionHistoryService _connectionHistory;

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

    // Sidebar navigation state
    private enum NavItem { Home, Devices, Files, Chat }
    private NavItem _currentNav = NavItem.Home;
    private View? _contentArea;
    private Button? _navHomeButton;
    private Button? _navDevicesButton;
    private Button? _navFilesButton;
    private Button? _navChatButton;

    // Devices panel references
    private StackLayout? _savedDeviceListLayout;
    private Label? _savedEmptyLabel;
    private StackLayout? _devDiscoveredListLayout;
    private Label? _devEmptyLabel;
    private Label? _devCountLabel;
    private Border? _devConnectionBanner;
    private Label? _devConnectionBannerLabel;
    private bool _devicesLoaded;

    // Files panel references
    private Label? _filesConnectionLabel;
    private Button? _filesSendButton;
    private StackLayout? _filesIncomingLayout;
    private Label? _filesIncomingEmptyLabel;
    private StackLayout? _filesTransfersLayout;
    private Label? _filesTransfersEmptyLabel;
    private ICommunicationService? _filesBoundComm;
    private IFileTransferService? _filesTransferService;
    private readonly Dictionary<string, FileTransferRequest> _filesPendingRequests = new();
    private readonly Dictionary<string, FilesTransferItem> _filesTransferItems = new();

    // Chat panel references
    private ScrollView? _chatScrollView;
    private StackLayout? _chatMessageList;
    private Entry? _chatMessageEntry;
    private Button? _chatSendButton;
    private Label? _chatStatusLabel;
    private Label? _chatEmptyState;

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
        RemoteDesktopMultiSessionManager multiSessionManager,
        Func<ChatPage> chatPageFactory,
        Func<SessionWorkspacePage> sessionWorkspacePageFactory,
        DeviceInfo localDevice,
        ISavedDevicesService savedDevices,
        IConnectionHistoryService connectionHistory)
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
        _multiSessionManager = multiSessionManager;
        _chatPageFactory = chatPageFactory;
        _sessionWorkspacePageFactory = sessionWorkspacePageFactory;
        _localDevice = localDevice;
        _savedDevices = savedDevices;
        _connectionHistory = connectionHistory;

        _deviceNumericId = DeviceIdentityManager.GetPreferredDisplayId(_localDevice);

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

        await EnsureClientDiscoveryAsync();

        // When returning from the viewer page (after disconnect), reset connect UI
        if (!_client.IsConnected && _isConnecting)
        {
            _isConnecting = false;
            SetPartnerStatus("Disconnected", Colors.Gray);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_connectButton != null)
                {
                    _connectButton.Text = "Open Session";
                    _connectButton.BackgroundColor = ThemeColors.Accent;
                    _connectButton.IsEnabled = true;
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

    private async Task EnsureClientDiscoveryAsync()
    {
        if (_client.IsStarted)
            return;

        try
        {
            await _client.StartListeningOnlyAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Client discovery start failed");
        }
    }

    // ── Layout ─────────────────────────────────────────────────────────

    private View BuildLayout()
    {
        _contentArea = BuildNavContent(_currentNav);

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),   // Header
                new RowDefinition(GridLength.Star),   // Sidebar + Content
                new RowDefinition(GridLength.Auto),   // Status bar
            },
            Padding = new Thickness(0),
            RowSpacing = 0
        };

        var bodyGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(72)),   // Sidebar
                new ColumnDefinition(GridLength.Star),       // Content area
            },
            ColumnSpacing = 0
        };

        bodyGrid.Add(BuildSidebar(), 0, 0);
        bodyGrid.Add(_contentArea, 1, 0);

        root.Add(BuildHeader(), 0, 0);
        root.Add(CreateGridChild(bodyGrid, row: 1), 0, 1);
        root.Add(CreateGridChild(BuildStatusBar(), row: 2), 0, 2);

        return root;
    }

    private View BuildSidebar()
    {
        _navHomeButton = BuildSidebarButton("\ud83c\udfe0", "Home", NavItem.Home);
        _navDevicesButton = BuildSidebarButton("\ud83d\udda5", "Devices", NavItem.Devices);
        _navFilesButton = BuildSidebarButton("\ud83d\udcc1", "Files", NavItem.Files);
        _navChatButton = BuildSidebarButton("\ud83d\udcac", "Chat", NavItem.Chat);

        UpdateSidebarSelection();

        var sidebarContent = new StackLayout
        {
            Spacing = 2,
            Children = { _navHomeButton, _navDevicesButton, _navFilesButton, _navChatButton }
        };

        // Use a Grid with a 1px right border line
        var sidebarGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(1)),
            },
            Children =
            {
                new ScrollView
                {
                    BackgroundColor = ThemeColors.SidebarBackground,
                    Padding = new Thickness(0, 8),
                    Content = sidebarContent
                },
                CreateGridChild(new BoxView { Color = ThemeColors.SidebarBorder, WidthRequest = 1 }, column: 1)
            }
        };

        return sidebarGrid;
    }

    private Button BuildSidebarButton(string icon, string label, NavItem nav)
    {
        var button = new Button
        {
            Text = $"{icon}\n{label}",
            FontSize = 11,
            LineBreakMode = LineBreakMode.WordWrap,
            BackgroundColor = Colors.Transparent,
            TextColor = ThemeColors.SidebarIconInactive,
            CornerRadius = 8,
            Padding = new Thickness(4, 10),
            Margin = new Thickness(6, 0),
            HeightRequest = 60,
            HorizontalOptions = LayoutOptions.Fill
        };
        button.Clicked += (_, _) => NavigateTo(nav);
        return button;
    }

    private void NavigateTo(NavItem nav)
    {
        if (_currentNav == nav) return;
        _currentNav = nav;

        // Detach files events if leaving Files
        DetachFilesTransferEvents();

        UpdateSidebarSelection();

        _contentArea = BuildNavContent(nav);
        // Replace content area in the body grid
        if (Content is Grid rootGrid && rootGrid.Children.Count > 1)
        {
            var bodyGrid = rootGrid.Children[1] as Grid;
            if (bodyGrid != null && bodyGrid.Children.Count > 1)
            {
                bodyGrid.Children.RemoveAt(1);
                bodyGrid.Add(_contentArea, 1, 0);
            }
        }
    }

    private void UpdateSidebarSelection()
    {
        SetSidebarButtonState(_navHomeButton, _currentNav == NavItem.Home);
        SetSidebarButtonState(_navDevicesButton, _currentNav == NavItem.Devices);
        SetSidebarButtonState(_navFilesButton, _currentNav == NavItem.Files);
        SetSidebarButtonState(_navChatButton, _currentNav == NavItem.Chat);
    }

    private static void SetSidebarButtonState(Button? button, bool active)
    {
        if (button == null) return;
        button.BackgroundColor = active ? ThemeColors.SidebarItemActive : Colors.Transparent;
        button.TextColor = active ? ThemeColors.Accent : ThemeColors.SidebarIconInactive;
        button.FontAttributes = active ? FontAttributes.Bold : FontAttributes.None;
    }

    private View BuildNavContent(NavItem nav)
    {
        return nav switch
        {
            NavItem.Home => BuildHomeContent(),
            NavItem.Devices => BuildDevicesContent(),
            NavItem.Files => BuildFilesContent(),
            NavItem.Chat => BuildChatContent(),
            _ => BuildHomeContent()
        };
    }

    private View BuildHomeContent()
    {
        var homeGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),   // Dashboard panels
                new RowDefinition(GridLength.Star),   // Connection status
                new RowDefinition(GridLength.Auto),   // Start/Stop button
            },
            Padding = new Thickness(0),
            RowSpacing = 0
        };

        homeGrid.Add(BuildDashboardPanels(), 0, 0);
        homeGrid.Add(CreateGridChild(BuildConnectionPanel(), row: 1), 0, 1);
        homeGrid.Add(CreateGridChild(BuildControlPanel(), row: 2), 0, 2);

        return homeGrid;
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
                    _qrCodeImage,
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
            Text = "Open Session",
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

    // ── Devices panel ───────────────────────────────────────────────────

    private View BuildDevicesContent()
    {
        var root = new StackLayout { Padding = new Thickness(20), Spacing = 14 };

        root.Add(new Label
        {
            Text = "Devices",
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.Accent,
            Margin = new Thickness(0, 0, 0, 4)
        });

        // Connection banner
        _devConnectionBannerLabel = new Label
        {
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.SuccessText,
        };
        _devConnectionBanner = new Border
        {
            BackgroundColor = ThemeColors.SuccessBackground,
            Stroke = ThemeColors.SuccessBorder,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(12),
            IsVisible = false,
            Content = _devConnectionBannerLabel
        };
        root.Add(_devConnectionBanner);

        // Address Book section
        root.Add(new Label
        {
            Text = "Address Book",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.TextPrimary,
            Margin = new Thickness(0, 4, 0, 0)
        });
        root.Add(new Label
        {
            Text = "Saved devices for quick reconnection",
            FontSize = 12,
            TextColor = ThemeColors.TextSecondary,
            Margin = new Thickness(0, 0, 0, 4)
        });

        _savedEmptyLabel = new Label
        {
            Text = "No saved devices yet.\nConnect to a host and save it from the Nearby section.",
            FontSize = 13,
            TextColor = ThemeColors.TextSecondary,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8)
        };
        _savedDeviceListLayout = new StackLayout { Spacing = 8 };
        _savedDeviceListLayout.Add(_savedEmptyLabel);
        root.Add(_savedDeviceListLayout);

        // Divider
        root.Add(new BoxView
        {
            Color = ThemeColors.Divider,
            HeightRequest = 1,
            HorizontalOptions = LayoutOptions.Fill,
            Margin = new Thickness(0, 4)
        });

        // Nearby Devices section
        root.Add(new Label
        {
            Text = "Nearby Devices",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.TextPrimary
        });

        _devCountLabel = new Label
        {
            Text = "Scanning for devices on your network...",
            FontSize = 12,
            TextColor = ThemeColors.TextSecondary,
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.Add(_devCountLabel);

        _devEmptyLabel = new Label
        {
            Text = "No devices found yet.\nMake sure RemoteLink Desktop is running on the same network.",
            FontSize = 13,
            TextColor = ThemeColors.TextSecondary,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 12)
        };
        _devDiscoveredListLayout = new StackLayout { Spacing = 8 };
        _devDiscoveredListLayout.Add(_devEmptyLabel);
        root.Add(_devDiscoveredListLayout);

        // Load data
        _ = LoadDevicesPanelAsync();

        return new ScrollView { Content = root };
    }

    private async Task LoadDevicesPanelAsync()
    {
        if (!_devicesLoaded)
        {
            await _savedDevices.LoadAsync();
            _devicesLoaded = true;
        }
        RefreshSavedDeviceCards();
        RefreshDiscoveredDeviceCards();
        UpdateDevicesConnectionBanner();
    }

    private void UpdateDevicesConnectionBanner()
    {
        if (_devConnectionBanner == null || _devConnectionBannerLabel == null) return;

        _devConnectionBanner.IsVisible = _client.IsConnected;
        if (_client.IsConnected)
        {
            var host = _client.ConnectedHost;
            var id = DeviceIdentityManager.FormatInternetDeviceId(host?.InternetDeviceId ?? host?.DeviceId);
            _devConnectionBannerLabel.Text = string.IsNullOrWhiteSpace(id)
                ? $"Connected to {host?.DeviceName ?? "Unknown"}"
                : $"Connected to {host?.DeviceName ?? "Unknown"} ({id})";
        }
    }

    private void RefreshSavedDeviceCards()
    {
        if (_savedDeviceListLayout == null) return;
        _savedDeviceListLayout.Clear();

        var saved = _savedDevices.GetAll();
        if (saved.Count == 0)
        {
            _savedDeviceListLayout.Add(_savedEmptyLabel);
            return;
        }

        foreach (var device in saved)
            _savedDeviceListLayout.Add(BuildDesktopSavedDeviceCard(device));
    }

    private View BuildDesktopSavedDeviceCard(SavedDevice saved)
    {
        var isConnected = _client.IsConnected && DeviceIdentityManager.MatchesDevice(saved, _client.ConnectedHost);

        var card = new Border
        {
            BackgroundColor = isConnected ? ThemeColors.SelectedCardBackground : ThemeColors.AddressBookBackground,
            Stroke = isConnected ? ThemeColors.SelectedCardBorder : ThemeColors.AddressBookBorder,
            StrokeThickness = isConnected ? 2 : 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14),
            Shadow = new Shadow { Brush = new SolidColorBrush(ThemeColors.ShadowColor), Offset = new Point(0, 2), Radius = 6 }
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 12,
            VerticalOptions = LayoutOptions.Center
        };

        var starIcon = new Label { Text = "\u2b50", FontSize = 22, VerticalOptions = LayoutOptions.Center };
        Grid.SetColumn(starIcon, 0);

        var infoStack = new StackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        var displayName = !string.IsNullOrWhiteSpace(saved.FriendlyName) ? saved.FriendlyName : saved.DeviceName;
        infoStack.Add(new Label { Text = displayName, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = ThemeColors.TextPrimary });

        if (!string.IsNullOrWhiteSpace(saved.FriendlyName) && saved.FriendlyName != saved.DeviceName)
            infoStack.Add(new Label { Text = saved.DeviceName, FontSize = 11, TextColor = ThemeColors.TextSecondary, FontAttributes = FontAttributes.Italic });

        infoStack.Add(new Label { Text = $"{saved.IPAddress}:{saved.Port}", FontSize = 12, TextColor = ThemeColors.TextSecondary });

        var formattedId = DeviceIdentityManager.FormatInternetDeviceId(saved.InternetDeviceId);
        if (!string.IsNullOrWhiteSpace(formattedId))
            infoStack.Add(new Label { Text = $"ID: {formattedId}", FontSize = 11, TextColor = ThemeColors.Accent });

        if (saved.LastConnected.HasValue)
            infoStack.Add(new Label { Text = $"Last connected: {FormatRelativeTime(saved.LastConnected.Value)}", FontSize = 10, TextColor = ThemeColors.TextMuted });

        if (isConnected)
            infoStack.Add(new Label { Text = "Currently connected", FontSize = 11, TextColor = ThemeColors.Accent, FontAttributes = FontAttributes.Italic });

        Grid.SetColumn(infoStack, 1);

        var actionsStack = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };

        var connectBtn = new Button
        {
            Text = isConnected ? "Connected" : "Connect",
            FontSize = 11,
            BackgroundColor = isConnected ? ThemeColors.SuccessBackground : ThemeColors.Accent,
            TextColor = isConnected ? ThemeColors.SuccessText : Colors.White,
            CornerRadius = 6,
            Padding = new Thickness(12, 4),
            HeightRequest = 30,
            IsEnabled = !isConnected
        };
        connectBtn.Clicked += async (_, _) => await OnSavedDeviceConnectAsync(saved);

        var editBtn = new Button
        {
            Text = "\u270f",
            FontSize = 12,
            BackgroundColor = ThemeColors.SecondaryButtonBackground,
            TextColor = ThemeColors.Accent,
            CornerRadius = 4,
            WidthRequest = 32,
            HeightRequest = 30,
            Padding = 0
        };
        editBtn.Clicked += async (_, _) => await OnEditSavedDeviceDesktop(saved);

        var deleteBtn = new Button
        {
            Text = "\ud83d\uddd1",
            FontSize = 12,
            BackgroundColor = ThemeColors.DangerSoft,
            TextColor = ThemeColors.Danger,
            CornerRadius = 4,
            WidthRequest = 32,
            HeightRequest = 30,
            Padding = 0
        };
        deleteBtn.Clicked += async (_, _) => await OnDeleteSavedDeviceDesktop(saved);

        actionsStack.Add(connectBtn);
        actionsStack.Add(editBtn);
        actionsStack.Add(deleteBtn);
        Grid.SetColumn(actionsStack, 2);

        grid.Add(starIcon);
        grid.Add(infoStack);
        grid.Add(actionsStack);
        card.Content = grid;

        return card;
    }

    private void RefreshDiscoveredDeviceCards()
    {
        if (_devDiscoveredListLayout == null) return;
        _devDiscoveredListLayout.Clear();

        List<DeviceInfo> hosts;
        lock (_discoveredHosts)
        {
            hosts = _discoveredHosts.ToList();
        }

        if (hosts.Count == 0)
        {
            _devDiscoveredListLayout.Add(_devEmptyLabel);
            if (_devCountLabel != null) _devCountLabel.Text = "Scanning for devices on your network...";
            return;
        }

        if (_devCountLabel != null) _devCountLabel.Text = $"{hosts.Count} device(s) found";

        foreach (var device in hosts)
            _devDiscoveredListLayout.Add(BuildDesktopDiscoveredCard(device));
    }

    private View BuildDesktopDiscoveredCard(DeviceInfo device)
    {
        var isConnected = _client.IsConnected && DeviceIdentityManager.MatchesDevice(device, _client.ConnectedHost);
        var isSaved = _savedDevices.FindMatchingDevice(device) != null;

        var card = new Border
        {
            BackgroundColor = isConnected ? ThemeColors.SelectedCardBackground : ThemeColors.CardBackground,
            Stroke = isConnected ? ThemeColors.SelectedCardBorder : ThemeColors.CardBorder,
            StrokeThickness = isConnected ? 2 : 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14),
            Shadow = new Shadow { Brush = new SolidColorBrush(ThemeColors.ShadowColor), Offset = new Point(0, 2), Radius = 6 }
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 12,
            VerticalOptions = LayoutOptions.Center
        };

        var icon = new Label
        {
            Text = device.Type == DeviceType.Desktop ? "\ud83d\udda5" : "\ud83d\udcf1",
            FontSize = 24,
            VerticalOptions = LayoutOptions.Center
        };
        Grid.SetColumn(icon, 0);

        var infoStack = new StackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        infoStack.Add(new Label { Text = device.DeviceName, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = ThemeColors.TextPrimary });
        infoStack.Add(new Label { Text = $"{device.IPAddress}:{device.Port}", FontSize = 12, TextColor = ThemeColors.TextSecondary });

        var fmtId = DeviceIdentityManager.FormatInternetDeviceId(device.InternetDeviceId);
        if (!string.IsNullOrWhiteSpace(fmtId))
            infoStack.Add(new Label { Text = $"ID: {fmtId}", FontSize = 11, TextColor = ThemeColors.Accent });

        if (isConnected)
            infoStack.Add(new Label { Text = "Currently connected", FontSize = 11, TextColor = ThemeColors.Accent, FontAttributes = FontAttributes.Italic });

        Grid.SetColumn(infoStack, 1);

        var rightStack = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };

        if (!isSaved)
        {
            var saveBtn = new Button
            {
                Text = "\u2b50 Save",
                FontSize = 11,
                BackgroundColor = ThemeColors.WarningSoft,
                TextColor = ThemeColors.WarningText,
                CornerRadius = 4,
                HeightRequest = 28,
                Padding = new Thickness(8, 0)
            };
            saveBtn.Clicked += async (_, _) => await OnSaveDiscoveredDeviceDesktop(device);
            rightStack.Add(saveBtn);
        }
        else
        {
            rightStack.Add(new Label { Text = "\u2b50 Saved", FontSize = 11, TextColor = ThemeColors.WarningText, VerticalOptions = LayoutOptions.Center });
        }

        var connectBtn = new Button
        {
            Text = isConnected ? "Connected" : "Connect",
            FontSize = 11,
            BackgroundColor = isConnected ? ThemeColors.SuccessBackground : ThemeColors.Accent,
            TextColor = isConnected ? ThemeColors.SuccessText : Colors.White,
            CornerRadius = 6,
            Padding = new Thickness(12, 4),
            HeightRequest = 28,
            IsEnabled = !isConnected
        };
        connectBtn.Clicked += async (_, _) => await OnDiscoveredDeviceConnectAsync(device);
        rightStack.Add(connectBtn);

        Grid.SetColumn(rightStack, 2);

        grid.Add(icon);
        grid.Add(infoStack);
        grid.Add(rightStack);
        card.Content = grid;

        return card;
    }

    private async Task OnSavedDeviceConnectAsync(SavedDevice saved)
    {
        if (_client.IsConnected && DeviceIdentityManager.MatchesDevice(saved, _client.ConnectedHost))
            return;

        var pin = await DisplayPromptAsync(
            title: $"Connect to {saved.FriendlyName ?? saved.DeviceName}",
            message: "Enter the 6-digit PIN shown on the remote host:",
            accept: "Connect",
            cancel: "Cancel",
            placeholder: "123456",
            maxLength: 6,
            keyboard: Keyboard.Numeric);

        if (string.IsNullOrWhiteSpace(pin)) return;

        var deviceInfo = new DeviceInfo
        {
            DeviceId = saved.DeviceId,
            InternetDeviceId = saved.InternetDeviceId,
            DeviceName = saved.DeviceName,
            IPAddress = saved.IPAddress,
            Port = saved.Port,
            SupportsRelay = saved.SupportsRelay,
            RelayServerHost = saved.RelayServerHost,
            RelayServerPort = saved.RelayServerPort,
            Type = saved.Type
        };

        try
        {
            var session = await _multiSessionManager.ConnectAsync(deviceInfo, pin);
            await _savedDevices.TouchLastConnectedAsync(saved.DeviceId);

            RefreshSavedDeviceCards();
            RefreshDiscoveredDeviceCards();
            UpdateDevicesConnectionBanner();

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var workspacePage = _sessionWorkspacePageFactory();
                workspacePage.FocusSession(session.SessionId);
                await Navigation.PushAsync(workspacePage);
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to saved device");
            await DisplayAlertAsync("Connection Failed", "Could not connect. Check the PIN and try again.", "OK");
        }
    }

    private async Task OnDiscoveredDeviceConnectAsync(DeviceInfo device)
    {
        if (_client.IsConnected && DeviceIdentityManager.MatchesDevice(device, _client.ConnectedHost))
            return;

        var pin = await DisplayPromptAsync(
            title: $"Connect to {device.DeviceName}",
            message: "Enter the 6-digit PIN shown on the remote host:",
            accept: "Connect",
            cancel: "Cancel",
            placeholder: "123456",
            maxLength: 6,
            keyboard: Keyboard.Numeric);

        if (string.IsNullOrWhiteSpace(pin)) return;

        try
        {
            var session = await _multiSessionManager.ConnectAsync(device, pin);

            // Auto-save device on success
            var existing = _savedDevices.FindMatchingDevice(device);
            if (existing != null)
            {
                existing.DeviceId = device.DeviceId;
                existing.DeviceName = device.DeviceName;
                existing.InternetDeviceId = DeviceIdentityManager.NormalizeInternetDeviceId(device.InternetDeviceId);
                existing.IPAddress = device.IPAddress;
                existing.Port = device.Port;
                existing.SupportsRelay = device.SupportsRelay;
                existing.RelayServerHost = device.RelayServerHost;
                existing.RelayServerPort = device.RelayServerPort;
                existing.Type = device.Type;
                existing.LastConnected = DateTime.UtcNow;
                await _savedDevices.AddOrUpdateAsync(existing);
            }
            else
            {
                await _savedDevices.AddOrUpdateAsync(new SavedDevice
                {
                    FriendlyName = device.DeviceName,
                    DeviceName = device.DeviceName,
                    DeviceId = device.DeviceId,
                    InternetDeviceId = DeviceIdentityManager.NormalizeInternetDeviceId(device.InternetDeviceId),
                    IPAddress = device.IPAddress,
                    Port = device.Port,
                    SupportsRelay = device.SupportsRelay,
                    RelayServerHost = device.RelayServerHost,
                    RelayServerPort = device.RelayServerPort,
                    Type = device.Type,
                    LastConnected = DateTime.UtcNow,
                    DateAdded = DateTime.UtcNow
                });
            }

            RefreshSavedDeviceCards();
            RefreshDiscoveredDeviceCards();
            UpdateDevicesConnectionBanner();

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var workspacePage = _sessionWorkspacePageFactory();
                workspacePage.FocusSession(session.SessionId);
                await Navigation.PushAsync(workspacePage);
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to discovered device");
            await DisplayAlertAsync("Connection Failed", "Could not connect. Check the PIN and try again.", "OK");
        }
    }

    private async Task OnEditSavedDeviceDesktop(SavedDevice saved)
    {
        var newName = await DisplayPromptAsync(
            title: "Edit Device Name",
            message: $"Enter a friendly name for {saved.DeviceName}:",
            accept: "Save",
            cancel: "Cancel",
            placeholder: saved.FriendlyName ?? saved.DeviceName,
            initialValue: saved.FriendlyName ?? saved.DeviceName,
            maxLength: 50);

        if (string.IsNullOrWhiteSpace(newName)) return;
        saved.FriendlyName = newName.Trim();
        await _savedDevices.AddOrUpdateAsync(saved);
        RefreshSavedDeviceCards();
    }

    private async Task OnDeleteSavedDeviceDesktop(SavedDevice saved)
    {
        var confirm = await DisplayAlertAsync(
            "Remove Device",
            $"Remove \"{saved.FriendlyName ?? saved.DeviceName}\" from your address book?",
            "Remove", "Cancel");

        if (!confirm) return;
        await _savedDevices.RemoveAsync(saved.Id);
        RefreshSavedDeviceCards();
    }

    private async Task OnSaveDiscoveredDeviceDesktop(DeviceInfo device)
    {
        var friendlyName = await DisplayPromptAsync(
            title: "Save Device",
            message: $"Enter a friendly name for {device.DeviceName}:",
            accept: "Save",
            cancel: "Cancel",
            placeholder: device.DeviceName,
            initialValue: device.DeviceName,
            maxLength: 50);

        if (string.IsNullOrWhiteSpace(friendlyName)) return;

        await _savedDevices.AddOrUpdateAsync(new SavedDevice
        {
            FriendlyName = friendlyName.Trim(),
            DeviceName = device.DeviceName,
            DeviceId = device.DeviceId,
            InternetDeviceId = DeviceIdentityManager.NormalizeInternetDeviceId(device.InternetDeviceId),
            IPAddress = device.IPAddress,
            Port = device.Port,
            SupportsRelay = device.SupportsRelay,
            RelayServerHost = device.RelayServerHost,
            RelayServerPort = device.RelayServerPort,
            Type = device.Type,
            DateAdded = DateTime.UtcNow
        });

        RefreshSavedDeviceCards();
        RefreshDiscoveredDeviceCards();
    }

    private static string FormatRelativeTime(DateTime utcTime)
    {
        var elapsed = DateTime.UtcNow - utcTime;
        if (elapsed.TotalMinutes < 1) return "just now";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays < 7) return $"{(int)elapsed.TotalDays}d ago";
        return utcTime.ToLocalTime().ToString("MMM d, yyyy");
    }

    // ── Files panel ─────────────────────────────────────────────────────

    private View BuildFilesContent()
    {
        var root = new StackLayout { Padding = new Thickness(20), Spacing = 14 };

        root.Add(new Label
        {
            Text = "Files",
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.Accent,
            Margin = new Thickness(0, 0, 0, 4)
        });

        root.Add(new Label
        {
            Text = "Send files to connected sessions or accept incoming transfers.",
            FontSize = 13,
            TextColor = ThemeColors.TextSecondary
        });

        // Connection status card
        _filesConnectionLabel = new Label { FontSize = 13, TextColor = ThemeColors.Accent };
        root.Add(new Border
        {
            BackgroundColor = ThemeColors.SurfaceBackground,
            Stroke = ThemeColors.ToolbarBorder,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(14),
            Content = new StackLayout
            {
                Spacing = 6,
                Children =
                {
                    new Label { Text = "Connection", FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = ThemeColors.TextPrimary },
                    _filesConnectionLabel
                }
            }
        });

        // Send card
        _filesSendButton = new Button
        {
            Text = "Browse and Send File",
            BackgroundColor = ThemeColors.Accent,
            TextColor = Colors.White,
            CornerRadius = 10,
            HeightRequest = 44
        };
        _filesSendButton.Clicked += OnFilesSendClicked;

        root.Add(new Border
        {
            BackgroundColor = ThemeColors.CardBackground,
            Stroke = ThemeColors.CardBorder,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(14),
            Content = new StackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Label { Text = "Send File", FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = ThemeColors.TextPrimary },
                    new Label { Text = "Choose a file and send it to the connected remote session.", FontSize = 13, TextColor = ThemeColors.TextSecondary },
                    _filesSendButton
                }
            }
        });

        // Incoming requests section
        _filesIncomingLayout = new StackLayout { Spacing = 8 };
        _filesIncomingEmptyLabel = new Label
        {
            Text = "No incoming file requests.",
            FontSize = 13,
            TextColor = ThemeColors.TextSecondary,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8)
        };

        root.Add(new Border
        {
            BackgroundColor = ThemeColors.CardBackground,
            Stroke = ThemeColors.CardBorder,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(14),
            Content = new StackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Label { Text = "Incoming Requests", FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = ThemeColors.TextPrimary },
                    _filesIncomingLayout
                }
            }
        });

        // Transfer activity section
        _filesTransfersLayout = new StackLayout { Spacing = 8 };
        _filesTransfersEmptyLabel = new Label
        {
            Text = "No transfers yet.",
            FontSize = 13,
            TextColor = ThemeColors.TextSecondary,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8)
        };

        root.Add(new Border
        {
            BackgroundColor = ThemeColors.CardBackground,
            Stroke = ThemeColors.CardBorder,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(14),
            Content = new StackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Label { Text = "Transfer Activity", FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = ThemeColors.TextPrimary },
                    _filesTransfersLayout
                }
            }
        });

        EnsureFilesTransferService();
        UpdateFilesConnectionUi();
        RefreshFilesIncoming();
        RefreshFilesTransfers();

        return new ScrollView { Content = root };
    }

    private void EnsureFilesTransferService()
    {
        var commService = _client.CurrentCommunicationService;
        if (!_client.IsConnected || commService is null)
        {
            if (_client.ConnectionState == ClientConnectionState.Disconnected)
            {
                DetachFilesTransferEvents();
                _filesBoundComm = null;
                _filesTransferService = null;
            }
            return;
        }

        if (ReferenceEquals(_filesBoundComm, commService) && _filesTransferService is not null)
            return;

        DetachFilesTransferEvents();
        _filesBoundComm = commService;
        _filesTransferService = new FileTransferService(
            Handler?.MauiContext?.Services.GetRequiredService<ILogger<FileTransferService>>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FileTransferService>.Instance,
            commService);
        _filesTransferService.TransferRequested += OnFilesTransferRequested;
        _filesTransferService.TransferResponseReceived += OnFilesTransferResponse;
        _filesTransferService.ProgressUpdated += OnFilesProgress;
        _filesTransferService.TransferCompleted += OnFilesTransferCompleted;
    }

    private void DetachFilesTransferEvents()
    {
        if (_filesTransferService is null) return;
        _filesTransferService.TransferRequested -= OnFilesTransferRequested;
        _filesTransferService.TransferResponseReceived -= OnFilesTransferResponse;
        _filesTransferService.ProgressUpdated -= OnFilesProgress;
        _filesTransferService.TransferCompleted -= OnFilesTransferCompleted;
    }

    private void UpdateFilesConnectionUi()
    {
        if (_filesConnectionLabel == null) return;
        var connected = _activeConnectionCount > 0;
        _filesConnectionLabel.Text = connected
            ? $"{_activeConnectionCount} active connection(s). File transfer ready."
            : "No active connections. Start a session to transfer files.";
        if (_filesSendButton != null)
        {
            _filesSendButton.IsEnabled = connected;
            _filesSendButton.BackgroundColor = connected ? ThemeColors.Accent : ThemeColors.NeutralButtonBackground;
        }
    }

    private async void OnFilesSendClicked(object? sender, EventArgs e)
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

            var transferId = await _fileTransfer.InitiateTransferAsync(result.FullPath, FileTransferDirection.Upload);

            _filesTransferItems[transferId] = new FilesTransferItem
            {
                TransferId = transferId,
                FileName = result.FileName,
                DirectionLabel = "Outgoing",
                Status = "Waiting for approval...",
                TotalBytes = new FileInfo(result.FullPath).Length,
                UpdatedAt = DateTime.UtcNow
            };

            RefreshFilesTransfers();
            _logger.LogInformation("File transfer initiated: {Id} for {File}", transferId, result.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File transfer failed");
            await DisplayAlertAsync("Error", $"File transfer failed: {ex.Message}", "OK");
        }
    }

    private void OnFilesTransferRequested(object? sender, FileTransferRequest request)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _filesPendingRequests[request.TransferId] = request;
            _filesTransferItems[request.TransferId] = new FilesTransferItem
            {
                TransferId = request.TransferId,
                FileName = request.FileName,
                DirectionLabel = "Incoming",
                Status = "Awaiting your approval",
                TotalBytes = request.FileSize,
                UpdatedAt = DateTime.UtcNow
            };
            RefreshFilesIncoming();
            RefreshFilesTransfers();
        });
    }

    private void OnFilesTransferResponse(object? sender, FileTransferResponse response)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_filesTransferItems.TryGetValue(response.TransferId, out var item)) return;
            item.Status = response.Accepted ? "Transferring..." : $"Declined: {response.Message ?? "Rejected"}";
            item.IsCompleted = !response.Accepted;
            item.UpdatedAt = DateTime.UtcNow;
            RefreshFilesTransfers();
        });
    }

    private void OnFilesProgress(object? sender, FileTransferProgress progress)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_filesTransferItems.TryGetValue(progress.TransferId, out var item))
            {
                item = new FilesTransferItem { TransferId = progress.TransferId, FileName = "Transfer", DirectionLabel = "Transfer" };
                _filesTransferItems[progress.TransferId] = item;
            }
            item.BytesTransferred = progress.BytesTransferred;
            item.TotalBytes = progress.TotalBytes > 0 ? progress.TotalBytes : item.TotalBytes;
            item.Status = item.IsCompleted ? item.Status : "Transferring...";
            item.BytesPerSecond = progress.BytesPerSecond;
            item.UpdatedAt = DateTime.UtcNow;
            RefreshFilesTransfers();
        });
    }

    private void OnFilesTransferCompleted(object? sender, FileTransferComplete complete)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _filesPendingRequests.Remove(complete.TransferId);
            if (!_filesTransferItems.TryGetValue(complete.TransferId, out var item))
            {
                item = new FilesTransferItem { TransferId = complete.TransferId, FileName = "Transfer", DirectionLabel = "Transfer" };
                _filesTransferItems[complete.TransferId] = item;
            }
            item.IsCompleted = true;
            item.IsSuccessful = complete.Success;
            item.Status = complete.Success ? "Completed" : (complete.ErrorMessage ?? "Transfer failed");
            item.SavedPath = complete.SavedPath ?? item.SavedPath;
            if (item.TotalBytes > 0 && complete.Success) item.BytesTransferred = item.TotalBytes;
            item.UpdatedAt = DateTime.UtcNow;
            RefreshFilesIncoming();
            RefreshFilesTransfers();
        });
    }

    private void RefreshFilesIncoming()
    {
        if (_filesIncomingLayout == null) return;
        _filesIncomingLayout.Children.Clear();

        if (_filesPendingRequests.Count == 0)
        {
            _filesIncomingLayout.Children.Add(_filesIncomingEmptyLabel);
            return;
        }

        foreach (var request in _filesPendingRequests.Values)
        {
            var acceptBtn = new Button
            {
                Text = "Accept",
                BackgroundColor = ThemeColors.Success,
                TextColor = Colors.White,
                CornerRadius = 8,
                WidthRequest = 100,
                HeightRequest = 32
            };
            var tid = request.TransferId;
            acceptBtn.Clicked += async (_, _) =>
            {
                if (_filesTransferService != null && _filesPendingRequests.TryGetValue(tid, out var req))
                {
                    var savePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "RemoteLink Transfers",
                        SanitizeFileName(req.FileName));
                    Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                    await _filesTransferService.AcceptTransferAsync(tid, savePath);
                    _filesPendingRequests.Remove(tid);
                    if (_filesTransferItems.TryGetValue(tid, out var item))
                    {
                        item.Status = "Receiving...";
                        item.SavedPath = savePath;
                    }
                    RefreshFilesIncoming();
                    RefreshFilesTransfers();
                }
            };

            var rejectBtn = new Button
            {
                Text = "Decline",
                BackgroundColor = ThemeColors.Danger,
                TextColor = Colors.White,
                CornerRadius = 8,
                WidthRequest = 100,
                HeightRequest = 32
            };
            rejectBtn.Clicked += async (_, _) =>
            {
                if (_filesTransferService != null)
                {
                    await _filesTransferService.RejectTransferAsync(tid, FileTransferRejectionReason.UserDeclined);
                    _filesPendingRequests.Remove(tid);
                    if (_filesTransferItems.TryGetValue(tid, out var item))
                    {
                        item.IsCompleted = true;
                        item.Status = "Declined";
                    }
                    RefreshFilesIncoming();
                    RefreshFilesTransfers();
                }
            };

            _filesIncomingLayout.Children.Add(new Border
            {
                BackgroundColor = ThemeColors.PlaceholderBackground,
                Stroke = ThemeColors.CardBorder,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                Padding = new Thickness(12),
                Content = new StackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        new Label { Text = request.FileName, FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = ThemeColors.TextPrimary },
                        new Label { Text = $"{FormatBytes(request.FileSize)} \u2022 {request.MimeType}", FontSize = 12, TextColor = ThemeColors.TextSecondary },
                        new HorizontalStackLayout { Spacing = 8, Children = { acceptBtn, rejectBtn } }
                    }
                }
            });
        }
    }

    private void RefreshFilesTransfers()
    {
        if (_filesTransfersLayout == null) return;
        _filesTransfersLayout.Children.Clear();

        if (_filesTransferItems.Count == 0)
        {
            _filesTransfersLayout.Children.Add(_filesTransfersEmptyLabel);
            return;
        }

        foreach (var item in _filesTransferItems.Values.OrderByDescending(i => i.UpdatedAt))
        {
            var progress = item.TotalBytes > 0 ? Math.Clamp(item.BytesTransferred / (double)item.TotalBytes, 0, 1) : 0;
            var statusColor = item.IsCompleted ? (item.IsSuccessful ? ThemeColors.SuccessText : ThemeColors.DangerText) : ThemeColors.Accent;
            var detailText = item.TotalBytes > 0
                ? $"{FormatBytes(item.BytesTransferred)} / {FormatBytes(item.TotalBytes)}"
                : "Waiting...";
            if (item.BytesPerSecond > 0 && !item.IsCompleted)
                detailText += $" \u2022 {FormatBytes(item.BytesPerSecond)}/s";

            var cardStack = new StackLayout
            {
                Spacing = 6,
                Children =
                {
                    new Label { Text = item.FileName, FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = ThemeColors.TextPrimary },
                    new Label { Text = item.DirectionLabel, FontSize = 12, TextColor = ThemeColors.TextSecondary },
                    new Label { Text = item.Status, FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = statusColor },
                    new ProgressBar { Progress = progress, ProgressColor = statusColor, BackgroundColor = ThemeColors.Divider, HeightRequest = 6 },
                    new Label { Text = detailText, FontSize = 12, TextColor = ThemeColors.TextSecondary }
                }
            };

            if (!string.IsNullOrWhiteSpace(item.SavedPath) && item.IsSuccessful)
                cardStack.Children.Add(new Label { Text = $"Saved to: {item.SavedPath}", FontSize = 11, TextColor = ThemeColors.TextSecondary, LineBreakMode = LineBreakMode.CharacterWrap });

            _filesTransfersLayout.Children.Add(new Border
            {
                BackgroundColor = ThemeColors.PlaceholderBackground,
                Stroke = ThemeColors.CardBorder,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                Padding = new Thickness(12),
                Content = cardStack
            });
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1) { value /= 1024; unitIndex++; }
        return unitIndex == 0 ? $"{value:0} {units[unitIndex]}" : $"{value:0.0} {units[unitIndex]}";
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(fileName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
    }

    private sealed class FilesTransferItem
    {
        public string TransferId { get; set; } = "";
        public string FileName { get; set; } = "";
        public string DirectionLabel { get; set; } = "";
        public string Status { get; set; } = "";
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public long BytesPerSecond { get; set; }
        public string? SavedPath { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsSuccessful { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // ── Chat panel ──────────────────────────────────────────────────────

    private View BuildChatContent()
    {
        _chatMessageList = new StackLayout { Spacing = 8, Padding = new Thickness(0, 8) };
        _chatEmptyState = new Label
        {
            Text = "No messages yet. Say hello to the connected host!",
            FontSize = 14,
            TextColor = ThemeColors.TextSecondary,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 24)
        };

        _chatScrollView = new ScrollView { Content = _chatMessageList, VerticalOptions = LayoutOptions.Fill };

        _chatStatusLabel = new Label { FontSize = 13, TextColor = ThemeColors.TextSecondary };

        _chatMessageEntry = new Entry
        {
            Placeholder = "Type a message...",
            HorizontalOptions = LayoutOptions.Fill,
            ReturnType = ReturnType.Send,
            TextColor = ThemeColors.EntryText,
            PlaceholderColor = ThemeColors.EntryPlaceholder,
        };
        _chatMessageEntry.Completed += OnChatSendClicked;
        _chatMessageEntry.TextChanged += (_, _) => UpdateChatSendState();

        _chatSendButton = new Button
        {
            Text = "Send",
            BackgroundColor = ThemeColors.NeutralButtonBackground,
            TextColor = Colors.White,
            CornerRadius = 8,
            WidthRequest = 80,
            HeightRequest = 38,
            IsEnabled = false
        };
        _chatSendButton.Clicked += OnChatSendClicked;

        var composerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10,
            Children = { _chatMessageEntry, CreateGridChild(_chatSendButton, column: 1) }
        };

        var composerBorder = new Border
        {
            BackgroundColor = ThemeColors.CardBackground,
            Stroke = ThemeColors.CardBorder,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(12),
            Margin = new Thickness(0, 12, 0, 0),
            Content = composerGrid
        };

        var headerBorder = new Border
        {
            BackgroundColor = ThemeColors.CardBackgroundAlt,
            Stroke = ThemeColors.ToolbarBorder,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(16, 14),
            Margin = new Thickness(0, 0, 0, 12),
            Content = new StackLayout
            {
                Spacing = 4,
                Children =
                {
                    new Label { Text = "Chat", FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = ThemeColors.Accent },
                    _chatStatusLabel
                }
            }
        };

        var messageAreaBorder = new Border
        {
            BackgroundColor = ThemeColors.PlaceholderBackground,
            Stroke = ThemeColors.CardBorder,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(14, 8),
            Content = new Grid { Children = { _chatScrollView, _chatEmptyState } }
        };

        var chatGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            Padding = new Thickness(20),
            Children =
            {
                headerBorder,
                CreateGridChild(messageAreaBorder, row: 1),
                CreateGridChild(composerBorder, row: 2)
            }
        };

        RefreshChatMessages();
        UpdateChatStatus();
        UpdateChatSendState();

        // Mark messages as read
        foreach (var msg in _messaging.GetMessages().Where(m => !m.IsRead))
            _ = _messaging.MarkAsReadAsync(msg.MessageId);
        UpdateChatButton();

        return chatGrid;
    }

    private void RefreshChatMessages()
    {
        if (_chatMessageList == null) return;
        _chatMessageList.Children.Clear();

        var messages = _messaging.GetMessages();
        foreach (var msg in messages)
            _chatMessageList.Children.Add(BuildChatBubble(msg));

        if (_chatEmptyState != null)
            _chatEmptyState.IsVisible = messages.Count == 0;

        if (messages.Count > 0 && _chatScrollView != null)
            _ = _chatScrollView.ScrollToAsync(0, double.MaxValue, false);
    }

    private View BuildChatBubble(ChatMessage message)
    {
        bool isLocal = message.SenderName == Environment.MachineName;

        var bubble = new Border
        {
            Padding = new Thickness(12, 8),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            BackgroundColor = isLocal ? ThemeColors.Accent : ThemeColors.ChatBubbleRemote,
            Stroke = isLocal ? Colors.Transparent : ThemeColors.ChatBubbleRemoteBorder,
            StrokeThickness = 1,
            MaximumWidthRequest = 400,
            Content = new StackLayout
            {
                Spacing = 3,
                Children =
                {
                    new Label { Text = message.SenderName, FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = isLocal ? ThemeColors.AccentText : ThemeColors.TextSecondary },
                    new Label { Text = message.Text, FontSize = 14, TextColor = isLocal ? Colors.White : ThemeColors.TextPrimary },
                    new Label { Text = message.Timestamp.ToLocalTime().ToString("HH:mm"), FontSize = 9, HorizontalOptions = LayoutOptions.End, TextColor = isLocal ? Color.FromArgb("#C0B0FF") : ThemeColors.TextMuted }
                }
            }
        };

        return new StackLayout
        {
            HorizontalOptions = isLocal ? LayoutOptions.End : LayoutOptions.Start,
            Children = { bubble }
        };
    }

    private async void OnChatSendClicked(object? sender, EventArgs e)
    {
        var text = _chatMessageEntry?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        if (_chatMessageEntry != null) _chatMessageEntry.Text = "";

        try
        {
            await _messaging.SendMessageAsync(text);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                RefreshChatMessages();
                UpdateChatSendState();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send chat message");
            await DisplayAlertAsync("Error", $"Failed to send: {ex.Message}", "OK");
        }
    }

    private void UpdateChatStatus()
    {
        if (_chatStatusLabel == null) return;
        _chatStatusLabel.Text = _activeConnectionCount > 0
            ? $"{_activeConnectionCount} active connection(s)"
            : "Connect to a remote host to start chatting.";
        if (_chatMessageEntry != null) _chatMessageEntry.IsEnabled = _activeConnectionCount > 0;
    }

    private void UpdateChatSendState()
    {
        if (_chatSendButton == null) return;
        _chatSendButton.IsEnabled = _activeConnectionCount > 0 && !string.IsNullOrWhiteSpace(_chatMessageEntry?.Text);
        _chatSendButton.BackgroundColor = _chatSendButton.IsEnabled ? ThemeColors.Accent : ThemeColors.NeutralButtonBackground;
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
        RefreshQrCode();
    }

    private void RefreshQrCode()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_qrCodeImage == null) return;

            if (_currentPin == "------" || !_isRunning)
            {
                _qrCodeImage.IsVisible = false;
                _qrCodeImage.Source = null;
                return;
            }

            try
            {
                var localIp = !string.IsNullOrWhiteSpace(_localDevice.IPAddress)
                    ? _localDevice.IPAddress
                    : NetworkAddressResolver.GetPreferredIPv4Address() ?? IPAddress.Loopback.ToString();
                _localDevice.IPAddress = localIp;
                var payload = $"remotelink://connect?host={localIp}:12346&pin={_currentPin}";

                using var qrGenerator = new QRCodeGenerator();
                using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
                using var qrCode = new PngByteQRCode(qrCodeData);
                var pngBytes = qrCode.GetGraphic(4);

                _qrCodeImage.Source = ImageSource.FromStream(() => new MemoryStream(pngBytes));
                _qrCodeImage.IsVisible = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate QR code");
                _qrCodeImage.IsVisible = false;
            }
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
            _ = RefreshInternetDeviceIdDisplayAsync();
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

            // Refresh active nav panel when connection changes
            if (_currentNav == NavItem.Devices) { RefreshSavedDeviceCards(); RefreshDiscoveredDeviceCards(); UpdateDevicesConnectionBanner(); }
            if (_currentNav == NavItem.Files) { UpdateFilesConnectionUi(); }
            if (_currentNav == NavItem.Chat) { UpdateChatStatus(); UpdateChatSendState(); }
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

        var discoveredIndex = -1;
        var isNewHost = false;
        lock (_discoveredHosts)
        {
            discoveredIndex = _discoveredHosts.FindIndex(h => h.DeviceId == host.DeviceId);
            if (discoveredIndex >= 0)
                _discoveredHosts[discoveredIndex] = host;
            else
            {
                _discoveredHosts.Add(host);
                discoveredIndex = _discoveredHosts.Count - 1;
                isNewHost = true;
            }
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_discoveredHostsPicker == null)
                return;

            var displayName = BuildDiscoveredHostDisplayName(host);
            if (isNewHost || discoveredIndex >= _discoveredHostsPicker.Items.Count)
                _discoveredHostsPicker.Items.Add(displayName);
            else
                _discoveredHostsPicker.Items[discoveredIndex] = displayName;
        });

        _logger.LogInformation("Discovered remote host: {Name} at {IP}:{Port}",
            host.DeviceName, host.IPAddress, host.Port);

        // Refresh devices panel if active
        if (_currentNav == NavItem.Devices)
            MainThread.BeginInvokeOnMainThread(RefreshDiscoveredDeviceCards);
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

                // Refresh devices panel if active
                if (_currentNav == NavItem.Devices)
                    RefreshDiscoveredDeviceCards();
            });
        }
    }

    private static string BuildDiscoveredHostDisplayName(DeviceInfo host)
    {
        var formattedInternetId = DeviceIdentityManager.FormatInternetDeviceId(host.InternetDeviceId);
        return string.IsNullOrWhiteSpace(formattedInternetId)
            ? $"{host.DeviceName} ({host.IPAddress}:{host.Port})"
            : $"{host.DeviceName} ({formattedInternetId} • {host.IPAddress}:{host.Port})";
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
            return;

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
        SetPartnerStatus($"Opening session to {targetDevice.IPAddress}:{targetDevice.Port}...", ThemeColors.Warning);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_connectButton != null)
            {
                _connectButton.Text = "Opening...";
                _connectButton.BackgroundColor = ThemeColors.SecondaryButtonBackground;
                _connectButton.IsEnabled = false;
            }
        });

        try
        {
            var session = await _multiSessionManager.ConnectAsync(targetDevice, pin);
            SetPartnerStatus(
                $"Session ready for {targetDevice.DeviceName} ({_multiSessionManager.GetSessions().Count} active tab(s))",
                ThemeColors.Success);

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (_connectButton != null)
                {
                    _connectButton.Text = "Open Session";
                    _connectButton.BackgroundColor = ThemeColors.Accent;
                    _connectButton.IsEnabled = true;
                }

                var workspacePage = _sessionWorkspacePageFactory();
                workspacePage.FocusSession(session.SessionId);
                await Navigation.PushAsync(workspacePage);
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open remote session to {Host}", targetDevice.DeviceName);
            SetPartnerStatus(ex.Message, ThemeColors.Danger);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_connectButton != null)
                {
                    _connectButton.Text = "Open Session";
                    _connectButton.BackgroundColor = ThemeColors.Accent;
                    _connectButton.IsEnabled = true;
                }
            });
        }

        _isConnecting = false;
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
                _connectButton.Text = "Open Session";
                _connectButton.BackgroundColor = ThemeColors.Accent;
                _connectButton.IsEnabled = true;
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
                    _connectButton.Text = "Open Session";
                    _connectButton.BackgroundColor = ThemeColors.Accent;
                    _connectButton.IsEnabled = true;
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
        var strippedId = DeviceIdentityManager.NormalizeInternetDeviceId(partnerId);

        // Try to match against discovered hosts by numeric ID
        if (strippedId is not null)
        {
            lock (_discoveredHosts)
            {
                foreach (var host in _discoveredHosts)
                {
                    var hostNumericId = DeviceIdentityManager.NormalizeInternetDeviceId(host.InternetDeviceId)
                        ?? DeviceIdentityManager.NormalizeInternetDeviceId(DeviceIdentityManager.GenerateLegacyNumericId(host.DeviceName));
                    if (hostNumericId == strippedId)
                        return host;
                }
            }

            return new DeviceInfo
            {
                DeviceId = strippedId,
                InternetDeviceId = strippedId,
                DeviceName = DeviceIdentityManager.FormatInternetDeviceId(strippedId),
                Type = DeviceType.Desktop,
                SupportsRelay = true
            };
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

    private async Task RefreshInternetDeviceIdDisplayAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await Task.Delay(300);
            if (DeviceIdentityManager.NormalizeInternetDeviceId(_localDevice.InternetDeviceId) is null)
                continue;

            break;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _deviceNumericId = DeviceIdentityManager.GetPreferredDisplayId(_localDevice);
            if (_deviceIdLabel != null)
                _deviceIdLabel.Text = _deviceNumericId;
        });
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
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateChatButton();
            // If on chat panel, refresh inline and mark as read
            if (_currentNav == NavItem.Chat)
            {
                RefreshChatMessages();
                foreach (var msg in _messaging.GetMessages().Where(m => !m.IsRead))
                    _ = _messaging.MarkAsReadAsync(msg.MessageId);
                UpdateChatButton();
            }
        });
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
