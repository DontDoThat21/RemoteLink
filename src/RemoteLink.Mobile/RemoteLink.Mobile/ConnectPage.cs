using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
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

    // UI references — manual connect card (to hide when connected)
    private Border _manualConnectCard = null!;
    private View _scanQrButton = null!;
    private View _discoveredSection = null!;

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

    public ConnectPage(ILogger<ConnectPage> logger, RemoteDesktopClient client, INetworkDiscovery networkDiscovery, IConnectionHistoryService connectionHistory)
    {
        _logger = logger;
        _client = client;
        _networkDiscovery = networkDiscovery;
        _connectionHistory = connectionHistory;

        Title = "Connect";
        BackgroundColor = Colors.White;

        Content = new ScrollView { Content = BuildLayout() };

        // Subscribe to client events
        _client.DeviceDiscovered += OnDeviceDiscovered;
        _client.DeviceLost += OnDeviceLost;
        _client.ServiceStatusChanged += OnServiceStatusChanged;
        _client.ConnectionStateChanged += OnConnectionStateChanged;
        _client.PairingFailed += OnPairingFailed;
        _client.ScreenDataReceived += OnScreenDataReceived;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Reset manual connect UI when returning from a disconnected state
        if (!_client.IsConnected && _isManualConnecting)
        {
            _isManualConnecting = false;
            SetManualConnectButtonState("Connect", Color.FromArgb("#512BD4"), true);
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

    // ── Layout ─────────────────────────────────────────────────────────

    private View BuildLayout()
    {
        var root = new StackLayout
        {
            Padding = new Thickness(16),
            Spacing = 12
        };

        // Header
        root.Add(new Label
        {
            Text = "RemoteLink",
            FontSize = 26,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            TextColor = Color.FromArgb("#512BD4"),
            Margin = new Thickness(0, 8, 0, 0)
        });

        // Status row
        var statusRow = new StackLayout { Orientation = StackOrientation.Horizontal, Spacing = 8 };
        _activityIndicator = new ActivityIndicator
        {
            VerticalOptions = LayoutOptions.Center,
            Color = Color.FromArgb("#512BD4"),
            WidthRequest = 20,
            HeightRequest = 20
        };
        _activityIndicator.SetBinding(ActivityIndicator.IsRunningProperty,
            new Binding(nameof(IsDiscovering), source: this));

        _statusLabel = new Label
        {
            FontSize = 14,
            VerticalOptions = LayoutOptions.Center,
            TextColor = Colors.Gray
        };
        _statusLabel.SetBinding(Label.TextProperty,
            new Binding(nameof(StatusMessage), source: this));

        statusRow.Add(_activityIndicator);
        statusRow.Add(_statusLabel);
        root.Add(statusRow);

        // Connected banner (hidden until connected)
        _connectedBanner = new StackLayout
        {
            BackgroundColor = Color.FromArgb("#E8F5E9"),
            Padding = new Thickness(12),
            Spacing = 6,
            IsVisible = false
        };

        _connectedHostLabel = new Label
        {
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#2E7D32")
        };

        var disconnectButton = new Button
        {
            Text = "Disconnect",
            BackgroundColor = Color.FromArgb("#C62828"),
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
            TextColor = Colors.DarkGray
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

        return root;
    }

    // ── Manual connection card ──────────────────────────────────────────

    private Border BuildManualConnectCard()
    {
        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#F8F6FF"),
            Stroke = Color.FromArgb("#512BD4"),
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
            TextColor = Color.FromArgb("#512BD4")
        });

        stack.Add(new Label
        {
            Text = "Enter the Partner ID and PIN displayed on the desktop host.",
            FontSize = 12,
            TextColor = Colors.Gray,
            Margin = new Thickness(0, 0, 0, 4)
        });

        // Partner ID entry
        stack.Add(new Label
        {
            Text = "Partner ID",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#333333")
        });

        _partnerIdEntry = new Entry
        {
            Placeholder = "IP address, IP:Port, or 9-digit ID",
            FontSize = 15,
            Keyboard = Keyboard.Text,
            BackgroundColor = Colors.White,
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
            TextColor = Color.FromArgb("#333333"),
            Margin = new Thickness(0, 4, 0, 0)
        });

        _pinEntry = new Entry
        {
            Placeholder = "6-digit PIN",
            FontSize = 15,
            Keyboard = Keyboard.Numeric,
            MaxLength = 6,
            IsPassword = true,
            BackgroundColor = Colors.White,
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
            BackgroundColor = Color.FromArgb("#512BD4"),
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
            SetManualStatus("Invalid Partner ID. Use IP, IP:Port, or 9-digit ID.", Color.FromArgb("#C62828"));
            return;
        }

        _isManualConnecting = true;
        SetManualConnectButtonState("Connecting...", Color.FromArgb("#999999"), false);
        SetManualStatus($"Connecting to {targetDevice.IPAddress}:{targetDevice.Port}...", Color.FromArgb("#FF8F00"));
        StatusMessage = $"Connecting to {targetDevice.DeviceName}...";
        IsDiscovering = true;

        var success = await _client.ConnectToHostAsync(targetDevice, pin);

        IsDiscovering = false;

        if (success)
        {
            _connectionStartedAt = DateTime.UtcNow;
            await RecordConnectionAsync(targetDevice, ConnectionOutcome.Success);
            SetManualStatus("Connected!", Color.FromArgb("#2E7D32"));
            // Button stays disabled while connected; will reset on disconnect via OnAppearing
        }
        else
        {
            await RecordConnectionAsync(targetDevice, ConnectionOutcome.Failed);
            _isManualConnecting = false;
            SetManualConnectButtonState("Connect", Color.FromArgb("#512BD4"), true);
            SetManualStatus("Connection failed. Check Partner ID and PIN.", Color.FromArgb("#C62828"));
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
            if (_manualStatusLabel != null)
            {
                _manualStatusLabel.Text = message;
                _manualStatusLabel.TextColor = color;
                _manualStatusLabel.IsVisible = true;
            }
        });
    }

    /// <summary>
    /// Resolves a Partner ID string into a DeviceInfo.
    /// Accepts: 9-digit numeric ID (matched against discovered hosts),
    /// IP:Port (e.g. "192.168.1.5:12346"), or plain IP (defaults to port 12346).
    /// </summary>
    private DeviceInfo? ResolvePartner(string partnerId)
    {
        var stripped = partnerId.Replace(" ", "");

        // Try to match against discovered hosts by numeric ID
        foreach (var host in _availableHosts)
        {
            var hostNumericId = GenerateNumericId(host.DeviceName).Replace(" ", "");
            if (hostNumericId == stripped)
                return host;
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

    /// <summary>
    /// Generates a stable 9-digit numeric ID from a machine name (same algorithm as Desktop UI).
    /// </summary>
    private static string GenerateNumericId(string machineName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(machineName + "RemoteLink"));
        long value = Math.Abs(BitConverter.ToInt64(hash, 0));
        long id = (value % 900_000_000) + 100_000_000;
        var digits = id.ToString();
        return $"{digits[..3]} {digits[3..6]} {digits[6..]}";
    }

    // ── QR code scanner ────────────────────────────────────────────────

    private View BuildScanQrButton()
    {
        var button = new Button
        {
            Text = "\ud83d\udcf7  Scan QR Code",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = Color.FromArgb("#7C4DFF"),
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
                SetManualStatus("QR code scanned — tap Connect", Color.FromArgb("#2E7D32"));
            }
            else
            {
                SetManualStatus("Invalid QR code format.", Color.FromArgb("#C62828"));
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
                    Color = Color.FromArgb("#E0E0E0"),
                    HeightRequest = 1,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Fill
                },
                new Label
                {
                    Text = "or quick connect",
                    FontSize = 12,
                    TextColor = Colors.Gray,
                    VerticalOptions = LayoutOptions.Center
                },
                new BoxView
                {
                    Color = Color.FromArgb("#E0E0E0"),
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
            TextColor = Colors.DarkGray,
            Margin = new Thickness(0, 4, 0, 0)
        });

        _noHostsLabel = new Label
        {
            Text = "Scanning for desktop hosts...",
            FontSize = 13,
            TextColor = Colors.Gray,
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
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#DADCE0"),
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
            TextColor = Colors.Black
        });
        infoStack.Add(new Label
        {
            Text = $"{device.IPAddress}:{device.Port}",
            FontSize = 12,
            TextColor = Colors.Gray
        });

        var connectLabel = new Label
        {
            Text = "Connect >",
            FontSize = 13,
            TextColor = Color.FromArgb("#512BD4"),
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
            if (!_availableHosts.Any(h => h.DeviceId == device.DeviceId))
            {
                _availableHosts.Add(device);
                AddHostCard(device);
                StatusMessage = $"Found {_availableHosts.Count} host(s). Tap to connect.";
            }
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
                    _connectedHostLabel.Text = $"Connected to {hostName}";
                    _connectedBanner.IsVisible = true;
                    _remoteViewer.IsVisible = true;
                    _manualConnectCard.IsVisible = false;
                    _scanQrButton.IsVisible = false;
                    _discoveredSection.IsVisible = false;
                    StatusMessage = $"Connected to {hostName}";
                    break;

                case ClientConnectionState.Disconnected:
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
                    SetManualConnectButtonState("Connect", Color.FromArgb("#512BD4"), true);
                    _manualStatusLabel.IsVisible = false;
                    OnManualEntryChanged(null, null!);
                    if (StatusMessage.StartsWith("Connected"))
                        StatusMessage = "Disconnected. Scanning for hosts...";
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
                ForwardGesture(new TouchGestureData
                {
                    GestureType = TouchGestureType.Pan,
                    X = _panStartX + (float)e.TotalX,
                    Y = _panStartY + (float)e.TotalY,
                    DeltaX = (float)e.TotalX, DeltaY = (float)e.TotalY,
                    DisplayWidth = (float)surface.Width, DisplayHeight = (float)surface.Height
                });
                break;
        }
    }

    private void OnScrolled(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status != GestureStatus.Running || sender is not View surface) return;
        float pixelDelta = (float)((1.0 - e.Scale) * 80.0);
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
