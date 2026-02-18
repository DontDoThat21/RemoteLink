using System.ComponentModel;
using RemoteLink.Mobile.Services;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;

namespace RemoteLink.Mobile;

public partial class MainPage : ContentPage, INotifyPropertyChanged
{
    private bool _isDiscovering;
    private string _statusMessage = "Initializing...";
    private readonly List<DeviceInfo> _availableHosts = new();

    // Touch-to-mouse translation
    private readonly TouchToMouseTranslator _touchTranslator = new();
    private RemoteDesktopClient? _client;

    // Target desktop resolution (updated once a session is established)
    private int _desktopWidth = 1920;
    private int _desktopHeight = 1080;

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

    public List<DeviceInfo> AvailableHosts => _availableHosts;

    // ── Remote viewer surface ─────────────────────────────────────────────────

    /// <summary>
    /// The BoxView that acts as the remote desktop display area.
    /// Gesture recognizers are attached here to capture touch input.
    /// </summary>
    private BoxView RemoteViewer { get; set; } = null!;

    public MainPage()
    {
        Title = "RemoteLink Mobile";
        BackgroundColor = Colors.White;

        var mainLayout = new StackLayout
        {
            Padding = new Thickness(20),
            Spacing = 20,
            VerticalOptions = LayoutOptions.Center
        };

        // Title
        var titleLabel = new Label
        {
            Text = "RemoteLink Mobile Client",
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            TextColor = Colors.Blue
        };

        // Status
        var statusLabel = new Label
        {
            FontSize = 16,
            HorizontalOptions = LayoutOptions.Center,
            TextColor = Colors.Gray
        };
        statusLabel.SetBinding(Label.TextProperty, new Binding(nameof(StatusMessage), source: this));

        // Activity indicator
        var activityIndicator = new ActivityIndicator
        {
            HorizontalOptions = LayoutOptions.Center,
            Color = Colors.Blue
        };
        activityIndicator.SetBinding(
            ActivityIndicator.IsRunningProperty,
            new Binding(nameof(IsDiscovering), source: this));

        // Host list placeholder
        var hostListLabel = new Label
        {
            Text = "Discovered hosts will appear here",
            FontSize = 14,
            HorizontalOptions = LayoutOptions.Center,
            TextColor = Colors.Gray
        };

        // ── Remote viewer surface with touch gesture recognizers ──────────────
        RemoteViewer = new BoxView
        {
            Color = Colors.Black,
            HeightRequest = 240,
            HorizontalOptions = LayoutOptions.FillAndExpand,
            IsVisible = false  // hidden until connected
        };

        AttachGestureRecognizers(RemoteViewer);

        var remoteViewerLabel = new Label
        {
            Text = "Remote Desktop (tap to connect to a host first)",
            FontSize = 12,
            HorizontalOptions = LayoutOptions.Center,
            TextColor = Colors.Gray
        };

        // Command execution section (retained for demo/debug purposes)
        var commandSectionLabel = new Label
        {
            Text = "Command Execution",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            TextColor = Colors.DarkBlue,
            Margin = new Thickness(0, 20, 0, 10)
        };

        var commandEntry = new Entry
        {
            Placeholder = "Enter command (e.g., 'echo Hello World')",
            FontSize = 14,
            Margin = new Thickness(0, 5)
        };

        var workingDirectoryEntry = new Entry
        {
            Placeholder = "Working directory (optional)",
            FontSize = 14,
            Margin = new Thickness(0, 5)
        };

        var executeButton = new Button
        {
            Text = "Execute Command",
            BackgroundColor = Colors.Blue,
            TextColor = Colors.White,
            Margin = new Thickness(0, 10),
            IsEnabled = false
        };

        var resultLabel = new Label
        {
            Text = "Command results will appear here",
            FontSize = 12,
            TextColor = Colors.Gray,
            Margin = new Thickness(0, 10)
        };

        commandEntry.TextChanged += (s, e) =>
        {
            executeButton.IsEnabled = !string.IsNullOrWhiteSpace(e.NewTextValue);
        };
        executeButton.Clicked += async (s, e) =>
            await ExecuteCommandAsync(commandEntry.Text, workingDirectoryEntry.Text, resultLabel);

        mainLayout.Children.Add(titleLabel);
        mainLayout.Children.Add(statusLabel);
        mainLayout.Children.Add(activityIndicator);
        mainLayout.Children.Add(hostListLabel);
        mainLayout.Children.Add(RemoteViewer);
        mainLayout.Children.Add(remoteViewerLabel);
        mainLayout.Children.Add(commandSectionLabel);
        mainLayout.Children.Add(commandEntry);
        mainLayout.Children.Add(workingDirectoryEntry);
        mainLayout.Children.Add(executeButton);
        mainLayout.Children.Add(resultLabel);

        Content = new ScrollView { Content = mainLayout };

        _ = StartDiscoveryAsync();
    }

    // ── Gesture recognizer wiring ─────────────────────────────────────────────

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

        // Two-finger scroll → mouse wheel  (PinchGestureRecognizer is used for
        // scroll because MAUI's ScrollGestureRecognizer is platform-limited)
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnScrolled;
        surface.GestureRecognizers.Add(pinch);
    }

    // ── Gesture handlers ──────────────────────────────────────────────────────

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not View surface) return;
        var pos = e.GetPosition(surface) ?? new Point(0, 0);

        var gesture = new TouchGestureData
        {
            GestureType = TouchGestureType.Tap,
            X = (float)pos.X,
            Y = (float)pos.Y,
            DisplayWidth = (float)surface.Width,
            DisplayHeight = (float)surface.Height
        };
        ForwardGesture(gesture);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not View surface) return;
        var pos = e.GetPosition(surface) ?? new Point(0, 0);

        var gesture = new TouchGestureData
        {
            GestureType = TouchGestureType.DoubleTap,
            X = (float)pos.X,
            Y = (float)pos.Y,
            DisplayWidth = (float)surface.Width,
            DisplayHeight = (float)surface.Height
        };
        ForwardGesture(gesture);
    }

    /// <summary>
    /// Pan gesture handler — translates the current pan position into a
    /// MouseMove event.  The <see cref="PanUpdatedEventArgs.TotalX"/> /
    /// <see cref="PanUpdatedEventArgs.TotalY"/> values are deltas from the
    /// start of the gesture; we track the running position ourselves.
    /// </summary>
    private float _panStartX, _panStartY;

    private void OnPanned(object? sender, PanUpdatedEventArgs e)
    {
        if (sender is not View surface) return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                // Capture the starting point (centre of surface as fallback)
                _panStartX = (float)(surface.Width / 2);
                _panStartY = (float)(surface.Height / 2);
                break;

            case GestureStatus.Running:
                var gesture = new TouchGestureData
                {
                    GestureType = TouchGestureType.Pan,
                    X = _panStartX + (float)e.TotalX,
                    Y = _panStartY + (float)e.TotalY,
                    DeltaX = (float)e.TotalX,
                    DeltaY = (float)e.TotalY,
                    DisplayWidth = (float)surface.Width,
                    DisplayHeight = (float)surface.Height
                };
                ForwardGesture(gesture);
                break;
        }
    }

    /// <summary>
    /// Pinch/scroll gesture handler.  We repurpose the pinch recognizer for
    /// two-finger scroll: when scale &lt; 1 we scroll down, &gt; 1 we scroll up.
    /// </summary>
    private void OnScrolled(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status != GestureStatus.Running) return;
        if (sender is not View surface) return;

        // Convert scale change to a pixel delta:
        //   scale > 1 → fingers moving apart → scroll up (negative DeltaY)
        //   scale < 1 → fingers pinching in → scroll down (positive DeltaY)
        float pixelDelta = (float)((1.0 - e.Scale) * 80.0);

        var gesture = new TouchGestureData
        {
            GestureType = TouchGestureType.Scroll,
            X = (float)(e.ScaleOrigin.X * surface.Width),
            Y = (float)(e.ScaleOrigin.Y * surface.Height),
            DeltaY = pixelDelta,
            DisplayWidth = (float)surface.Width,
            DisplayHeight = (float)surface.Height
        };
        ForwardGesture(gesture);
    }

    // ── Touch → host forwarding ───────────────────────────────────────────────

    /// <summary>
    /// Translates a touch gesture via <see cref="TouchToMouseTranslator"/> and
    /// sends each resulting <see cref="InputEvent"/> to the desktop host.
    /// No-ops when no host is connected.
    /// </summary>
    private void ForwardGesture(TouchGestureData gesture)
    {
        if (_client is null) return;

        var events = _touchTranslator.Translate(gesture, _desktopWidth, _desktopHeight);
        foreach (var inputEvent in events)
        {
            // Fire-and-forget; errors are logged inside RemoteDesktopClient.
            _ = _client.SendInputEventAsync(inputEvent);
        }
    }

    // ── Discovery ─────────────────────────────────────────────────────────────

    private async Task StartDiscoveryAsync()
    {
        try
        {
            StatusMessage = "Starting discovery service...";
            IsDiscovering = true;

            var localDevice = new DeviceInfo
            {
                DeviceId = Environment.MachineName + "_Mobile_" + Guid.NewGuid().ToString("N")[..8],
                DeviceName = Environment.MachineName + " Mobile",
                Type = DeviceType.Mobile,
                Port = 12347
            };
            var networkDiscovery = new RemoteLink.Shared.Services.UdpNetworkDiscovery(localDevice);
            _client = new RemoteDesktopClient(null!, networkDiscovery);

            _client.DeviceDiscovered += OnDeviceDiscovered;
            _client.DeviceLost += OnDeviceLost;
            _client.ServiceStatusChanged += OnServiceStatusChanged;

            await _client.StartAsync();
            StatusMessage = "Searching for desktop hosts...";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error starting discovery: {ex.Message}";
            IsDiscovering = false;
        }
    }

    private void OnDeviceDiscovered(object? sender, DeviceInfo device)
    {
        if (device.Type == DeviceType.Desktop)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!_availableHosts.Any(h => h.DeviceId == device.DeviceId))
                {
                    _availableHosts.Add(device);
                    StatusMessage = $"Found {_availableHosts.Count} desktop host(s)";
                    RemoteViewer.IsVisible = true;
                }
            });
        }
    }

    private void OnDeviceLost(object? sender, DeviceInfo device)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var existing = _availableHosts.FirstOrDefault(h => h.DeviceId == device.DeviceId);
            if (existing != null)
            {
                _availableHosts.Remove(existing);
                StatusMessage = $"Found {_availableHosts.Count} desktop host(s)";
                if (_availableHosts.Count == 0)
                    RemoteViewer.IsVisible = false;
            }
        });
    }

    private void OnServiceStatusChanged(object? sender, string status)
    {
        MainThread.BeginInvokeOnMainThread(() => { /* update status indicator if desired */ });
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected override void OnPropertyChanged(
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // ── Command execution (debug/demo) ─────────────────────────────────────────

    private async Task ExecuteCommandAsync(string command, string? workingDirectory, Label resultLabel)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            resultLabel.Text = "Please enter a command";
            resultLabel.TextColor = Colors.Red;
            return;
        }

        try
        {
            resultLabel.Text = "Executing command...";
            resultLabel.TextColor = Colors.Blue;

            var inputEvent = new InputEvent
            {
                Type = InputEventType.CommandExecution,
                Command = command,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory
            };

            await SimulateCommandExecutionAsync(inputEvent, resultLabel);
        }
        catch (Exception ex)
        {
            resultLabel.Text = $"Error: {ex.Message}";
            resultLabel.TextColor = Colors.Red;
        }
    }

    private static async Task SimulateCommandExecutionAsync(InputEvent inputEvent, Label resultLabel)
    {
        await Task.Delay(1000);

        var info = $"Command: {inputEvent.Command}\n";
        if (!string.IsNullOrEmpty(inputEvent.WorkingDirectory))
            info += $"Working Directory: {inputEvent.WorkingDirectory}\n";
        info += $"Event ID: {inputEvent.EventId}\n";
        info += "Status: Command sent to desktop host";

        resultLabel.Text = info;
        resultLabel.TextColor = Colors.Green;
    }
}
