using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using DeviceInfo = RemoteLink.Shared.Models.DeviceInfo;
using DeviceType = RemoteLink.Shared.Models.DeviceType;

namespace RemoteLink.Mobile;

/// <summary>
/// Connect tab: discovery status, discovered host quick-connect list,
/// remote desktop viewer with gesture-based input when connected.
/// </summary>
public class ConnectPage : ContentPage, INotifyPropertyChanged
{
    private readonly ILogger<ConnectPage> _logger;
    private readonly RemoteDesktopClient _client;
    private readonly INetworkDiscovery _networkDiscovery;

    // Touch-to-mouse translation
    private readonly TouchToMouseTranslator _touchTranslator = new();

    // Target desktop resolution
    private int _desktopWidth = 1920;
    private int _desktopHeight = 1080;

    // State
    private bool _isDiscovering;
    private string _statusMessage = "Initializing...";
    private readonly List<DeviceInfo> _availableHosts = new();

    // Throttle frame rendering
    private volatile bool _frameRenderBusy;

    // UI references
    private StackLayout _hostListContainer = null!;
    private Label _noHostsLabel = null!;
    private StackLayout _connectedBanner = null!;
    private Label _connectedHostLabel = null!;
    private Image _remoteViewer = null!;
    private Label _statusLabel = null!;
    private ActivityIndicator _activityIndicator = null!;

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

    public ConnectPage(ILogger<ConnectPage> logger, RemoteDesktopClient client, INetworkDiscovery networkDiscovery)
    {
        _logger = logger;
        _client = client;
        _networkDiscovery = networkDiscovery;

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

        // Discovered hosts (quick connect)
        root.Add(new Label
        {
            Text = "Discovered Hosts",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.DarkGray,
            Margin = new Thickness(0, 8, 0, 0)
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
        root.Add(_hostListContainer);

        return root;
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

        var nameLabel = new Label
        {
            Text = device.DeviceName,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.Black
        };
        var addrLabel = new Label
        {
            Text = $"{device.IPAddress}:{device.Port}",
            FontSize = 12,
            TextColor = Colors.Gray
        };
        var connectLabel = new Label
        {
            Text = "Tap to connect",
            FontSize = 12,
            TextColor = Color.FromArgb("#512BD4"),
            HorizontalOptions = LayoutOptions.End
        };

        var cardContent = new StackLayout { Spacing = 2 };
        cardContent.Add(nameLabel);
        cardContent.Add(addrLabel);
        cardContent.Add(connectLabel);
        card.Content = cardContent;

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

    // ── Connection flow ────────────────────────────────────────────────

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

        if (!success)
            await DisplayAlertAsync("Connection Failed", StatusMessage, "OK");
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
                    StatusMessage = $"Connected to {hostName}";
                    break;

                case ClientConnectionState.Disconnected:
                    _connectedBanner.IsVisible = false;
                    _remoteViewer.IsVisible = false;
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

    // ── INotifyPropertyChanged ─────────────────────────────────────────

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected override void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
