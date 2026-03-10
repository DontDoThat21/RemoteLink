using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;

namespace RemoteLink.Desktop.UI;

/// <summary>
/// Full remote desktop viewer page — displays the remote host's screen and forwards
/// mouse/keyboard input when connecting FROM the desktop to another host.
/// </summary>
public class RemoteViewerPage : ContentPage
{
    private readonly RemoteDesktopClient _client;
    private readonly ILogger<RemoteViewerPage> _logger;

    // Viewer
    private readonly Image _remoteViewer;
    private volatile bool _frameRenderBusy;
    private int _remoteWidth;
    private int _remoteHeight;

    // Toolbar labels
    private readonly Label _hostNameLabel;
    private readonly Label _fpsLabel;
    private readonly Label _latencyLabel;
    private readonly Label _resolutionLabel;
    private readonly Label _qualityLabel;

    // Keyboard capture
    private readonly Entry _keyCapture;

    // Metrics timer
    private IDispatcherTimer? _metricsTimer;
    private int _frameCount;
    private DateTime _lastFpsCheck = DateTime.UtcNow;

    // Track if we initiated the disconnect (vs. remote drop)
    private bool _isDisconnecting;

    public RemoteViewerPage(RemoteDesktopClient client, ILogger<RemoteViewerPage> logger)
    {
        _client = client;
        _logger = logger;

        Title = $"Remote Desktop — {_client.ConnectedHost?.DeviceName ?? "Unknown"}";
        BackgroundColor = Colors.Black;

        // ── Top toolbar ──────────────────────────────────────────────────────
        _hostNameLabel = new Label
        {
            Text = _client.ConnectedHost?.DeviceName ?? "Remote Host",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
        };

        _fpsLabel = new Label
        {
            Text = "FPS: --",
            FontSize = 11,
            TextColor = ThemeColors.ViewerFpsText,
            VerticalOptions = LayoutOptions.Center,
        };

        _latencyLabel = new Label
        {
            Text = "Latency: --",
            FontSize = 11,
            TextColor = ThemeColors.ViewerFpsText,
            VerticalOptions = LayoutOptions.Center,
        };

        var rebootButton = new Button
        {
            Text = "Reboot",
            BackgroundColor = ThemeColors.Warning,
            TextColor = Colors.White,
            CornerRadius = 4,
            HeightRequest = 32,
            FontSize = 12,
            Padding = new Thickness(12, 0),
        };
        rebootButton.Clicked += OnRemoteRebootClicked;

        var disconnectButton = new Button
        {
            Text = "Disconnect",
            BackgroundColor = ThemeColors.Danger,
            TextColor = Colors.White,
            CornerRadius = 4,
            HeightRequest = 32,
            FontSize = 12,
            Padding = new Thickness(12, 0),
        };
        disconnectButton.Clicked += OnDisconnectClicked;

        var toolbar = new Grid
        {
            BackgroundColor = ThemeColors.ViewerToolbar,
            Padding = new Thickness(12, 6),
            ColumnSpacing = 16,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),    // host name
                new ColumnDefinition(GridLength.Auto),    // FPS
                new ColumnDefinition(GridLength.Auto),    // latency
                new ColumnDefinition(GridLength.Auto),    // reboot
                new ColumnDefinition(GridLength.Star),    // spacer
                new ColumnDefinition(GridLength.Auto),    // disconnect
            },
            Children =
            {
                CreateGridChild(_hostNameLabel, column: 0),
                CreateGridChild(_fpsLabel, column: 1),
                CreateGridChild(_latencyLabel, column: 2),
                CreateGridChild(rebootButton, column: 3),
                CreateGridChild(disconnectButton, column: 5),
            }
        };

        // ── Remote viewer ────────────────────────────────────────────────────
        _remoteViewer = new Image
        {
            BackgroundColor = Colors.Black,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Aspect = Aspect.AspectFit,
        };

        // Mouse input via pointer gestures on the viewer
        var pointerGesture = new PointerGestureRecognizer();
        pointerGesture.PointerMoved += OnPointerMoved;
        pointerGesture.PointerPressed += OnPointerPressed;
        pointerGesture.PointerReleased += OnPointerReleased;
        _remoteViewer.GestureRecognizers.Add(pointerGesture);

        // Tap gesture as fallback for clicks
        var tapGesture = new TapGestureRecognizer { NumberOfTapsRequired = 1 };
        tapGesture.Tapped += OnViewerTapped;
        _remoteViewer.GestureRecognizers.Add(tapGesture);

        // ── Hidden entry for keyboard capture ────────────────────────────────
        _keyCapture = new Entry
        {
            Opacity = 0,
            HeightRequest = 0,
            WidthRequest = 0,
            IsReadOnly = false,
            Keyboard = Keyboard.Default,
        };
        _keyCapture.TextChanged += OnKeyCaptureTextChanged;

        // ── Bottom status bar ────────────────────────────────────────────────
        _resolutionLabel = new Label
        {
            Text = "Resolution: --",
            FontSize = 10,
            TextColor = ThemeColors.ViewerResolutionText,
            VerticalOptions = LayoutOptions.Center,
        };

        _qualityLabel = new Label
        {
            Text = "Connected",
            FontSize = 10,
            TextColor = ThemeColors.Success,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End,
        };

        var statusBar = new Grid
        {
            BackgroundColor = ThemeColors.ViewerStatusBar,
            Padding = new Thickness(12, 4),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            Children =
            {
                CreateGridChild(_resolutionLabel, column: 0),
                CreateGridChild(_qualityLabel, column: 1),
            }
        };

        // ── Page layout ──────────────────────────────────────────────────────
        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),    // toolbar
                new RowDefinition(GridLength.Star),    // viewer
                new RowDefinition(GridLength.Auto),    // hidden key capture
                new RowDefinition(GridLength.Auto),    // status bar
            },
            Children =
            {
                CreateGridChild(toolbar, row: 0),
                CreateGridChild(_remoteViewer, row: 1),
                CreateGridChild(_keyCapture, row: 2),
                CreateGridChild(statusBar, row: 3),
            }
        };

        // ── Event subscriptions ──────────────────────────────────────────────
        _client.ScreenDataReceived += OnScreenDataReceived;
        _client.ConnectionStateChanged += OnConnectionStateChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Focus the hidden entry to capture keyboard input
        _keyCapture.Focus();

        // Start metrics timer
        _metricsTimer = Dispatcher.CreateTimer();
        _metricsTimer.Interval = TimeSpan.FromSeconds(1);
        _metricsTimer.Tick += OnMetricsTick;
        _metricsTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Stop metrics
        _metricsTimer?.Stop();
        _metricsTimer = null;

        // Unsubscribe events
        _client.ScreenDataReceived -= OnScreenDataReceived;
        _client.ConnectionStateChanged -= OnConnectionStateChanged;
    }

    // ── Frame rendering ──────────────────────────────────────────────────────

    private void OnScreenDataReceived(object? sender, ScreenData screenData)
    {
        if (_frameRenderBusy) return;
        _frameRenderBusy = true;

        Interlocked.Increment(ref _frameCount);

        try
        {
            var stream = ScreenFrameConverter.ToImageStream(screenData);
            if (stream is null)
            {
                _frameRenderBusy = false;
                return;
            }

            if (screenData.Width > 0) _remoteWidth = screenData.Width;
            if (screenData.Height > 0) _remoteHeight = screenData.Height;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    _remoteViewer.Source = ImageSource.FromStream(() => stream);

                    // Update resolution label on change
                    if (_remoteWidth > 0 && _remoteHeight > 0)
                        _resolutionLabel.Text = $"Resolution: {_remoteWidth}x{_remoteHeight}";
                }
                finally
                {
                    _frameRenderBusy = false;
                }
            });
        }
        catch
        {
            _frameRenderBusy = false;
        }
    }

    // ── Metrics ──────────────────────────────────────────────────────────────

    private void OnMetricsTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastFpsCheck).TotalSeconds;
        if (elapsed >= 1.0)
        {
            var frames = Interlocked.Exchange(ref _frameCount, 0);
            var fps = (int)(frames / elapsed);
            _lastFpsCheck = now;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                _fpsLabel.Text = $"FPS: {fps}";
            });
        }

        // Update quality badge based on connection state
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _qualityLabel.Text = _client.IsConnected ? "Connected" : "Disconnected";
            _qualityLabel.TextColor = _client.IsConnected
                ? ThemeColors.Success
                : ThemeColors.Danger;
        });
    }

    // ── Mouse input forwarding ───────────────────────────────────────────────

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_client.IsConnected || _remoteWidth <= 0 || _remoteHeight <= 0) return;

        var position = e.GetPosition(_remoteViewer);
        if (position is null) return;

        var (remoteX, remoteY) = MapToRemoteCoordinates(position.Value.X, position.Value.Y);

        _ = _client.SendInputEventAsync(new InputEvent
        {
            Type = InputEventType.MouseMove,
            X = remoteX,
            Y = remoteY,
        });
    }

    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        if (!_client.IsConnected || _remoteWidth <= 0 || _remoteHeight <= 0) return;

        // Focus the key capture entry to ensure keyboard events are received
        _keyCapture.Focus();

        var position = e.GetPosition(_remoteViewer);
        if (position is null) return;

        var (remoteX, remoteY) = MapToRemoteCoordinates(position.Value.X, position.Value.Y);

        _ = _client.SendInputEventAsync(new InputEvent
        {
            Type = InputEventType.MouseClick,
            X = remoteX,
            Y = remoteY,
            IsPressed = true,
        });
    }

    private void OnPointerReleased(object? sender, PointerEventArgs e)
    {
        if (!_client.IsConnected || _remoteWidth <= 0 || _remoteHeight <= 0) return;

        var position = e.GetPosition(_remoteViewer);
        if (position is null) return;

        var (remoteX, remoteY) = MapToRemoteCoordinates(position.Value.X, position.Value.Y);

        _ = _client.SendInputEventAsync(new InputEvent
        {
            Type = InputEventType.MouseClick,
            X = remoteX,
            Y = remoteY,
            IsPressed = false,
        });
    }

    private void OnViewerTapped(object? sender, TappedEventArgs e)
    {
        // Re-focus key capture on tap so keyboard events keep flowing
        _keyCapture.Focus();
    }

    private (int x, int y) MapToRemoteCoordinates(double viewerX, double viewerY)
    {
        double viewerWidth = _remoteViewer.Width;
        double viewerHeight = _remoteViewer.Height;

        if (viewerWidth <= 0 || viewerHeight <= 0)
            return (0, 0);

        // Account for AspectFit — the image may have letterboxing
        double imageAspect = (double)_remoteWidth / _remoteHeight;
        double viewerAspect = viewerWidth / viewerHeight;

        double renderWidth, renderHeight, offsetX, offsetY;

        if (imageAspect > viewerAspect)
        {
            // Image is wider — horizontal fit, vertical letterbox
            renderWidth = viewerWidth;
            renderHeight = viewerWidth / imageAspect;
            offsetX = 0;
            offsetY = (viewerHeight - renderHeight) / 2;
        }
        else
        {
            // Image is taller — vertical fit, horizontal letterbox
            renderHeight = viewerHeight;
            renderWidth = viewerHeight * imageAspect;
            offsetX = (viewerWidth - renderWidth) / 2;
            offsetY = 0;
        }

        // Map pointer position to the rendered image area
        double relativeX = (viewerX - offsetX) / renderWidth;
        double relativeY = (viewerY - offsetY) / renderHeight;

        // Clamp to [0, 1]
        relativeX = Math.Clamp(relativeX, 0, 1);
        relativeY = Math.Clamp(relativeY, 0, 1);

        int remoteX = (int)Math.Round(relativeX * (_remoteWidth - 1));
        int remoteY = (int)Math.Round(relativeY * (_remoteHeight - 1));

        return (remoteX, remoteY);
    }

    // ── Keyboard input forwarding ────────────────────────────────────────────

    private void OnKeyCaptureTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_client.IsConnected) return;

        var newText = e.NewTextValue;
        var oldText = e.OldTextValue ?? "";

        if (string.IsNullOrEmpty(newText))
        {
            // Text was cleared — could be a backspace
            if (!string.IsNullOrEmpty(oldText))
            {
                _ = _client.SendInputEventAsync(new InputEvent
                {
                    Type = InputEventType.KeyPress,
                    KeyCode = "Back",
                    IsPressed = true,
                });
                _ = _client.SendInputEventAsync(new InputEvent
                {
                    Type = InputEventType.KeyRelease,
                    KeyCode = "Back",
                    IsPressed = false,
                });
            }
            return;
        }

        // Send each new character as a text input event
        var addedText = newText.Length > oldText.Length
            ? newText[oldText.Length..]
            : newText;

        foreach (var ch in addedText)
        {
            _ = _client.SendInputEventAsync(new InputEvent
            {
                Type = InputEventType.TextInput,
                Text = ch.ToString(),
            });
        }

        // Clear the entry after sending to keep it ready for more input
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _keyCapture.Text = "";
        });
    }

    // ── Connection state ─────────────────────────────────────────────────────

    private void OnConnectionStateChanged(object? sender, ClientConnectionState state)
    {
        if (state == ClientConnectionState.Disconnected && !_isDisconnecting)
        {
            if (_client.IsAutoReconnectPending)
                return;

            // Remote host dropped the connection — navigate back
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await this.DisplayAlertAsync("Disconnected", "The remote host has ended the session.", "OK");
                await Navigation.PopAsync();
            });
        }
    }

    private async void OnDisconnectClicked(object? sender, EventArgs e)
    {
        _isDisconnecting = true;

        try
        {
            await _client.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disconnect");
        }

        await Navigation.PopAsync();
    }

    private async void OnRemoteRebootClicked(object? sender, EventArgs e)
    {
        if (!_client.IsConnected)
            return;

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
            var message = response.AutoReconnectSupported == true
                ? $"Remote reboot requested. Waiting {response.ReconnectDelaySeconds ?? 25} seconds before reconnecting..."
                : "Remote reboot requested. Automatic reconnect is unavailable for this session.";

            await DisplayAlertAsync("Remote Reboot", message, "OK");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to request remote reboot");
            await DisplayAlertAsync("Remote Reboot", $"Failed to request remote reboot: {ex.Message}", "OK");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static View CreateGridChild(View view, int column = 0, int row = 0)
    {
        Grid.SetColumn(view, column);
        Grid.SetRow(view, row);
        return view;
    }
}
