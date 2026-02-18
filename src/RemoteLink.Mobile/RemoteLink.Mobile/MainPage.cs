using System.ComponentModel;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;

namespace RemoteLink.Mobile;

/// <summary>
/// Main page: combines discovery, host-list, connection flow, and the remote
/// desktop viewer surface in a single scrollable code-behind page.
/// </summary>
public partial class MainPage : ContentPage, INotifyPropertyChanged
{
    // ── State ─────────────────────────────────────────────────────────────────

    private bool _isDiscovering;
    private string _statusMessage = "Initializing...";
    private readonly List<DeviceInfo> _availableHosts = new();

    // Touch-to-mouse translation
    private readonly TouchToMouseTranslator _touchTranslator = new();
    private RemoteDesktopClient? _client;

    // Target desktop resolution (updated when a session is established)
    private int _desktopWidth  = 1920;
    private int _desktopHeight = 1080;

    // ── Dynamic UI references ─────────────────────────────────────────────────

    private StackLayout    _hostListContainer = null!;
    private Label          _noHostsLabel      = null!;
    private StackLayout    _connectedBanner   = null!;
    private Label          _connectedHostLabel = null!;
    private BoxView        _remoteViewer      = null!;

    // ── Bindable properties ───────────────────────────────────────────────────

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

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainPage()
    {
        Title = "RemoteLink Mobile";
        BackgroundColor = Colors.White;

        Content = new ScrollView { Content = BuildLayout() };

        _ = StartDiscoveryAsync();
    }

    // ── Layout builder ────────────────────────────────────────────────────────

    private View BuildLayout()
    {
        var root = new StackLayout
        {
            Padding = new Thickness(16),
            Spacing = 12
        };

        // ── Title ────────────────────────────────────────────────────────────
        root.Children.Add(new Label
        {
            Text              = "RemoteLink",
            FontSize          = 28,
            FontAttributes    = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            TextColor         = Color.FromArgb("#1A73E8"),
            Margin            = new Thickness(0, 12, 0, 0)
        });

        // ── Status row ───────────────────────────────────────────────────────
        var statusRow = new StackLayout { Orientation = StackOrientation.Horizontal, Spacing = 8 };
        var activityIndicator = new ActivityIndicator
        {
            VerticalOptions = LayoutOptions.Center,
            Color           = Color.FromArgb("#1A73E8"),
            WidthRequest    = 20,
            HeightRequest   = 20
        };
        activityIndicator.SetBinding(ActivityIndicator.IsRunningProperty,
            new Binding(nameof(IsDiscovering), source: this));

        var statusLabel = new Label
        {
            FontSize          = 14,
            VerticalOptions   = LayoutOptions.Center,
            TextColor         = Colors.Gray
        };
        statusLabel.SetBinding(Label.TextProperty,
            new Binding(nameof(StatusMessage), source: this));

        statusRow.Children.Add(activityIndicator);
        statusRow.Children.Add(statusLabel);
        root.Children.Add(statusRow);

        // ── Connected banner (hidden until connected) ─────────────────────────
        _connectedBanner = new StackLayout
        {
            BackgroundColor = Color.FromArgb("#E8F5E9"),
            Padding         = new Thickness(12),
            Spacing         = 6,
            IsVisible       = false
        };

        _connectedHostLabel = new Label
        {
            FontSize       = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor      = Color.FromArgb("#2E7D32")
        };

        var disconnectButton = new Button
        {
            Text            = "Disconnect",
            BackgroundColor = Color.FromArgb("#C62828"),
            TextColor       = Colors.White,
            FontSize        = 14,
            CornerRadius    = 6,
            HeightRequest   = 36,
            HorizontalOptions = LayoutOptions.Start
        };
        disconnectButton.Clicked += async (_, _) => await DisconnectFromHostAsync();

        _connectedBanner.Children.Add(_connectedHostLabel);
        _connectedBanner.Children.Add(disconnectButton);
        root.Children.Add(_connectedBanner);

        // ── Remote viewer surface ─────────────────────────────────────────────
        _remoteViewer = new BoxView
        {
            Color             = Colors.Black,
            HeightRequest     = 240,
            HorizontalOptions = LayoutOptions.FillAndExpand,
            IsVisible         = false,
            CornerRadius      = 6
        };
        AttachGestureRecognizers(_remoteViewer);

        root.Children.Add(new Label
        {
            Text      = "Remote Desktop",
            FontSize  = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.DarkGray
        });
        root.Children.Add(_remoteViewer);

        // ── Discovered hosts ──────────────────────────────────────────────────
        root.Children.Add(new Label
        {
            Text           = "Discovered Hosts",
            FontSize       = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor      = Colors.DarkGray,
            Margin         = new Thickness(0, 8, 0, 0)
        });

        _noHostsLabel = new Label
        {
            Text      = "Scanning for desktop hosts…",
            FontSize  = 13,
            TextColor = Colors.Gray,
            Margin    = new Thickness(4, 0)
        };

        _hostListContainer = new StackLayout { Spacing = 8 };
        _hostListContainer.Children.Add(_noHostsLabel);

        root.Children.Add(_hostListContainer);

        // ── Command execution section (debug / demo) ──────────────────────────
        root.Children.Add(BuildCommandSection());

        return root;
    }

    // ── Host list helpers ─────────────────────────────────────────────────────

    /// <summary>Creates a tappable card for <paramref name="device"/> and adds it to the list.</summary>
    private void AddHostCard(DeviceInfo device)
    {
        // Remove the "no hosts" placeholder once we have at least one
        _hostListContainer.Children.Remove(_noHostsLabel);

        var card = new Frame
        {
            BackgroundColor  = Colors.White,
            BorderColor      = Color.FromArgb("#DADCE0"),
            CornerRadius     = 8,
            Padding          = new Thickness(12),
            HasShadow        = true,
            AutomationId     = $"host-{device.DeviceId}"
        };

        var nameLabel = new Label
        {
            Text           = device.DeviceName,
            FontSize       = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor      = Colors.Black
        };
        var addrLabel = new Label
        {
            Text      = $"{device.IPAddress}:{device.Port}",
            FontSize  = 12,
            TextColor = Colors.Gray
        };
        var connectLabel = new Label
        {
            Text      = "Tap to connect →",
            FontSize  = 12,
            TextColor = Color.FromArgb("#1A73E8"),
            HorizontalOptions = LayoutOptions.End
        };

        var cardContent = new StackLayout { Spacing = 2 };
        cardContent.Children.Add(nameLabel);
        cardContent.Children.Add(addrLabel);
        cardContent.Children.Add(connectLabel);
        card.Content = cardContent;

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OnHostCardTappedAsync(device);
        card.GestureRecognizers.Add(tap);

        _hostListContainer.Children.Add(card);
    }

    /// <summary>Removes the card for <paramref name="device"/> from the list.</summary>
    private void RemoveHostCard(DeviceInfo device)
    {
        var toRemove = _hostListContainer.Children
            .OfType<Frame>()
            .FirstOrDefault(f => f.AutomationId == $"host-{device.DeviceId}");

        if (toRemove != null)
            _hostListContainer.Children.Remove(toRemove);

        // Re-add "no hosts" placeholder if the list is now empty
        if (!_hostListContainer.Children.OfType<Frame>().Any())
            _hostListContainer.Children.Add(_noHostsLabel);
    }

    // ── Connection flow ───────────────────────────────────────────────────────

    /// <summary>
    /// Called when a host card is tapped.  Prompts the user for the PIN shown
    /// on the desktop host and initiates the connection + pairing flow.
    /// </summary>
    private async Task OnHostCardTappedAsync(DeviceInfo host)
    {
        // If already connected to the same host, do nothing
        if (_client?.IsConnected == true &&
            _client.ConnectedHost?.DeviceId == host.DeviceId)
        {
            await DisplayAlert("Already Connected",
                $"You are already connected to {host.DeviceName}.", "OK");
            return;
        }

        // Prompt for PIN
        var pin = await DisplayPromptAsync(
            title:       $"Connect to {host.DeviceName}",
            message:     "Enter the 6-digit PIN shown on the desktop host:",
            accept:      "Connect",
            cancel:      "Cancel",
            placeholder: "123456",
            maxLength:   6,
            keyboard:    Keyboard.Numeric);

        if (string.IsNullOrWhiteSpace(pin)) return;   // user cancelled

        if (_client is null)
        {
            await DisplayAlert("Not Ready", "Discovery service is not running.", "OK");
            return;
        }

        // Disable all host cards during connection attempt
        StatusMessage = $"Connecting to {host.DeviceName}…";
        IsDiscovering = true;

        var success = await _client.ConnectToHostAsync(host, pin);

        IsDiscovering = false;

        if (!success)
        {
            // PairingFailed event will have already set StatusMessage
            await DisplayAlert("Connection Failed", StatusMessage, "OK");
        }
    }

    /// <summary>Disconnects from the current host and resets the UI.</summary>
    private async Task DisconnectFromHostAsync()
    {
        if (_client is null) return;
        await _client.DisconnectAsync();
    }

    // ── RemoteDesktopClient event handlers ────────────────────────────────────

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
                    : "Scanning for desktop hosts…";
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
                    var hostName = _client?.ConnectedHost?.DeviceName ?? "Unknown";
                    _connectedHostLabel.Text = $"✓ Connected to {hostName}";
                    _connectedBanner.IsVisible = true;
                    _remoteViewer.IsVisible    = true;
                    StatusMessage = $"Connected to {hostName}";
                    break;

                case ClientConnectionState.Disconnected:
                    _connectedBanner.IsVisible = false;
                    _remoteViewer.IsVisible    = false;
                    if (StatusMessage.StartsWith("Connected"))
                        StatusMessage = "Disconnected. Scanning for hosts…";
                    break;

                case ClientConnectionState.Connecting:
                    StatusMessage = $"Connecting to {_client?.ConnectedHost?.DeviceName}…";
                    break;

                case ClientConnectionState.Authenticating:
                    StatusMessage = "Authenticating…";
                    break;
            }
        });
    }

    private void OnPairingFailed(object? sender, string reason)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusMessage = $"⚠ {reason}";
        });
    }

    private void OnServiceStatusChanged(object? sender, string status)
    {
        MainThread.BeginInvokeOnMainThread(() => StatusMessage = status);
    }

    // ── Discovery startup ─────────────────────────────────────────────────────

    private async Task StartDiscoveryAsync()
    {
        try
        {
            StatusMessage  = "Starting discovery…";
            IsDiscovering  = true;

            var localDevice = new DeviceInfo
            {
                DeviceId   = $"{Environment.MachineName}_Mobile_{Guid.NewGuid():N}"[..Math.Min(48, 50)],
                DeviceName = $"{Environment.MachineName} Mobile",
                Type       = DeviceType.Mobile,
                Port       = 12347
            };

            var networkDiscovery = new UdpNetworkDiscovery(localDevice);
            _client = new RemoteDesktopClient(null, networkDiscovery);

            _client.DeviceDiscovered      += OnDeviceDiscovered;
            _client.DeviceLost            += OnDeviceLost;
            _client.ServiceStatusChanged  += OnServiceStatusChanged;
            _client.ConnectionStateChanged += OnConnectionStateChanged;
            _client.PairingFailed         += OnPairingFailed;

            await _client.StartAsync();

            StatusMessage = "Scanning for desktop hosts…";
            IsDiscovering = true;   // keep spinner visible while scanning
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            IsDiscovering = false;
        }
    }

    // ── Gesture recognizers ───────────────────────────────────────────────────

    /// <summary>
    /// Attaches all four gesture recognizers to the remote viewer surface.
    /// All handlers translate the gesture via <see cref="TouchToMouseTranslator"/>
    /// and forward the resulting <see cref="InputEvent"/> list to the host.
    /// </summary>
    private void AttachGestureRecognizers(View surface)
    {
        // Single tap → left click
        var tap = new TapGestureRecognizer { NumberOfTapsRequired = 1 };
        tap.Tapped += OnTapped;
        surface.GestureRecognizers.Add(tap);

        // Double tap → double-click
        var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        doubleTap.Tapped += OnDoubleTapped;
        surface.GestureRecognizers.Add(doubleTap);

        // Pan (drag) → mouse move
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanned;
        surface.GestureRecognizers.Add(pan);

        // Two-finger scroll → mouse wheel
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnScrolled;
        surface.GestureRecognizers.Add(pinch);
    }

    // ── Gesture handlers ──────────────────────────────────────────────────────

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not View surface) return;
        var pos = e.GetPosition(surface) ?? new Point(0, 0);

        ForwardGesture(new TouchGestureData
        {
            GestureType   = TouchGestureType.Tap,
            X             = (float)pos.X,
            Y             = (float)pos.Y,
            DisplayWidth  = (float)surface.Width,
            DisplayHeight = (float)surface.Height
        });
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not View surface) return;
        var pos = e.GetPosition(surface) ?? new Point(0, 0);

        ForwardGesture(new TouchGestureData
        {
            GestureType   = TouchGestureType.DoubleTap,
            X             = (float)pos.X,
            Y             = (float)pos.Y,
            DisplayWidth  = (float)surface.Width,
            DisplayHeight = (float)surface.Height
        });
    }

    private float _panStartX, _panStartY;

    private void OnPanned(object? sender, PanUpdatedEventArgs e)
    {
        if (sender is not View surface) return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartX = (float)(surface.Width  / 2);
                _panStartY = (float)(surface.Height / 2);
                break;

            case GestureStatus.Running:
                ForwardGesture(new TouchGestureData
                {
                    GestureType   = TouchGestureType.Pan,
                    X             = _panStartX + (float)e.TotalX,
                    Y             = _panStartY + (float)e.TotalY,
                    DeltaX        = (float)e.TotalX,
                    DeltaY        = (float)e.TotalY,
                    DisplayWidth  = (float)surface.Width,
                    DisplayHeight = (float)surface.Height
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
            GestureType   = TouchGestureType.Scroll,
            X             = (float)(e.ScaleOrigin.X * surface.Width),
            Y             = (float)(e.ScaleOrigin.Y * surface.Height),
            DeltaY        = pixelDelta,
            DisplayWidth  = (float)surface.Width,
            DisplayHeight = (float)surface.Height
        });
    }

    // ── Touch → host forwarding ───────────────────────────────────────────────

    private void ForwardGesture(TouchGestureData gesture)
    {
        if (_client is null || !_client.IsConnected) return;

        var events = _touchTranslator.Translate(gesture, _desktopWidth, _desktopHeight);
        foreach (var inputEvent in events)
            _ = _client.SendInputEventAsync(inputEvent);
    }

    // ── Command execution section (debug / demo) ──────────────────────────────

    private static View BuildCommandSection()
    {
        var section = new StackLayout { Spacing = 8, Margin = new Thickness(0, 16, 0, 0) };

        section.Children.Add(new Label
        {
            Text           = "Command Execution (Debug)",
            FontSize       = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor      = Colors.DarkGray
        });

        var commandEntry = new Entry
        {
            Placeholder = "e.g. echo Hello World",
            FontSize    = 13
        };
        var wdEntry = new Entry
        {
            Placeholder = "Working directory (optional)",
            FontSize    = 13
        };
        var resultLabel = new Label
        {
            Text      = "Results appear here",
            FontSize  = 12,
            TextColor = Colors.Gray
        };
        var executeButton = new Button
        {
            Text            = "Execute Command",
            BackgroundColor = Color.FromArgb("#1A73E8"),
            TextColor       = Colors.White,
            CornerRadius    = 6,
            IsEnabled       = false
        };

        commandEntry.TextChanged += (_, e) =>
            executeButton.IsEnabled = !string.IsNullOrWhiteSpace(e.NewTextValue);

        executeButton.Clicked += async (_, _) =>
            await ExecuteCommandAsync(commandEntry.Text, wdEntry.Text, resultLabel);

        section.Children.Add(commandEntry);
        section.Children.Add(wdEntry);
        section.Children.Add(executeButton);
        section.Children.Add(resultLabel);

        return section;
    }

    private static async Task ExecuteCommandAsync(
        string command, string? workingDirectory, Label resultLabel)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            resultLabel.Text      = "Please enter a command";
            resultLabel.TextColor = Colors.Red;
            return;
        }

        resultLabel.Text      = "Executing…";
        resultLabel.TextColor = Colors.Blue;

        await Task.Delay(500);

        var info  = $"Command: {command}\n";
        if (!string.IsNullOrEmpty(workingDirectory)) info += $"Dir: {workingDirectory}\n";
        info += "Status: would be forwarded to desktop host";

        resultLabel.Text      = info;
        resultLabel.TextColor = Colors.Green;
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected override void OnPropertyChanged(
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
