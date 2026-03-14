using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using System.Collections.Concurrent;
using System.Text;
#if WINDOWS
using System.Runtime.InteropServices;
using WinDragEventArgs = Microsoft.UI.Xaml.DragEventArgs;
using WinFrameworkElement = Microsoft.UI.Xaml.FrameworkElement;
using WinDataPackageOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation;
using WinPointerRoutedEventArgs = Microsoft.UI.Xaml.Input.PointerRoutedEventArgs;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
#endif

namespace RemoteLink.Desktop.UI;

/// <summary>
/// Full remote desktop viewer page — displays the remote host's screen and forwards
/// mouse/keyboard input when connecting FROM the desktop to another host.
/// </summary>
public class RemoteViewerPage : ContentPage
{
    private readonly RemoteDesktopClient _client;
    private readonly ILogger<RemoteViewerPage> _logger;
    private readonly Func<Task>? _closeSessionAsync;

    // Viewer
    private readonly GpuFrameView _gpuViewer;
    private volatile bool _frameRenderBusy;
    private int _remoteWidth;
    private int _remoteHeight;

    // Toolbar labels
    private readonly Label _hostNameLabel;
    private readonly Label _fpsLabel;
    private readonly Label _latencyLabel;
    private readonly Label _resolutionLabel;
    private readonly Label _qualityLabel;
    private readonly Label _transferStatusLabel;
    private readonly Border _dropOverlay;
    private RemoteFrameSnapshot? _latestSnapshot;

    // Keyboard capture
    private readonly Entry _keyCapture;

    // Metrics timer
    private IDispatcherTimer? _metricsTimer;
    private int _frameCount;
    private DateTime _lastFpsCheck = DateTime.UtcNow;

    // Track if we initiated the disconnect (vs. remote drop)
    private bool _isDisconnecting;
    private ICommunicationService? _boundCommunicationService;
    private IFileTransferService? _fileTransferService;
    private readonly ConcurrentDictionary<string, string> _pendingTransferNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _viewerCursorHidden;
#if WINDOWS
    private WinFrameworkElement? _nativeDropTarget;
#endif

    public RemoteViewerPage(RemoteDesktopClient client, ILogger<RemoteViewerPage> logger)
        : this(client, logger, null)
    {
    }

    public RemoteViewerPage(
        RemoteDesktopClient client,
        ILogger<RemoteViewerPage> logger,
        Func<Task>? closeSessionAsync)
    {
        _client = client;
        _logger = logger;
        _closeSessionAsync = closeSessionAsync;

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

        var systemInfoButton = new Button
        {
            Text = "System Info",
            BackgroundColor = ThemeColors.SecondaryButtonBackground,
            TextColor = ThemeColors.SecondaryButtonText,
            CornerRadius = 4,
            HeightRequest = 32,
            FontSize = 12,
            Padding = new Thickness(12, 0),
        };
        systemInfoButton.Clicked += OnSystemInfoClicked;

        var screenshotButton = new Button
        {
            Text = "Screenshot",
            BackgroundColor = ThemeColors.Success,
            TextColor = Colors.White,
            CornerRadius = 4,
            HeightRequest = 32,
            FontSize = 12,
            Padding = new Thickness(12, 0),
        };
        screenshotButton.Clicked += OnScreenshotClicked;

        var commandButton = new Button
        {
            Text = "Command",
            BackgroundColor = ThemeColors.SecondaryButtonBackground,
            TextColor = ThemeColors.SecondaryButtonText,
            CornerRadius = 4,
            HeightRequest = 32,
            FontSize = 12,
            Padding = new Thickness(12, 0),
        };
        commandButton.Clicked += OnRemoteCommandClicked;

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
                new ColumnDefinition(GridLength.Auto),    // screenshot
                new ColumnDefinition(GridLength.Auto),    // system info
                new ColumnDefinition(GridLength.Auto),    // command
                new ColumnDefinition(GridLength.Auto),    // reboot
                new ColumnDefinition(GridLength.Star),    // spacer
                new ColumnDefinition(GridLength.Auto),    // disconnect
            },
            Children =
            {
                CreateGridChild(_hostNameLabel, column: 0),
                CreateGridChild(_fpsLabel, column: 1),
                CreateGridChild(_latencyLabel, column: 2),
                CreateGridChild(screenshotButton, column: 3),
                CreateGridChild(systemInfoButton, column: 4),
                CreateGridChild(commandButton, column: 5),
                CreateGridChild(rebootButton, column: 6),
                CreateGridChild(disconnectButton, column: 8),
            }
        };

        // ── Remote viewer ────────────────────────────────────────────────────
        _gpuViewer = new GpuFrameView
        {
            BackgroundColor = Colors.Black,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };
        _gpuViewer.HandlerChanged += OnRemoteViewerHandlerChanged;

        // Mouse input via pointer gestures on the viewer
        var pointerGesture = new PointerGestureRecognizer();
        pointerGesture.PointerMoved += OnPointerMoved;
        pointerGesture.PointerPressed += OnPointerPressed;
        pointerGesture.PointerReleased += OnPointerReleased;
        _gpuViewer.GestureRecognizers.Add(pointerGesture);

        // Tap gesture as fallback for clicks
        var tapGesture = new TapGestureRecognizer { NumberOfTapsRequired = 1 };
        tapGesture.Tapped += OnViewerTapped;
        _gpuViewer.GestureRecognizers.Add(tapGesture);

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

        _transferStatusLabel = new Label
        {
            Text = "Drag files into the viewer to transfer",
            FontSize = 10,
            TextColor = ThemeColors.ViewerResolutionText,
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        _dropOverlay = new Border
        {
            IsVisible = false,
            InputTransparent = true,
            BackgroundColor = Color.FromRgba(81, 43, 212, 96),
            Stroke = Colors.White,
            StrokeThickness = 2,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Margin = new Thickness(24),
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = "Drop files to send",
                        FontSize = 22,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Colors.White,
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    new Label
                    {
                        Text = "Files will upload to the connected remote host.",
                        FontSize = 13,
                        TextColor = Colors.White,
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                }
            }
        };

        var viewerSurface = new Grid
        {
            Children =
            {
                _gpuViewer,
                _dropOverlay
            }
        };

        var statusBar = new Grid
        {
            BackgroundColor = ThemeColors.ViewerStatusBar,
            Padding = new Thickness(12, 4),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            Children =
            {
                CreateGridChild(_resolutionLabel, column: 0),
                CreateGridChild(_transferStatusLabel, column: 1),
                CreateGridChild(_qualityLabel, column: 2),
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
                CreateGridChild(viewerSurface, row: 1),
                CreateGridChild(_keyCapture, row: 2),
                CreateGridChild(statusBar, row: 3),
            }
        };

        }

        /// <summary>
        /// Activates the viewer: subscribes to data events, starts the metrics timer, and focuses keyboard capture.
        /// </summary>
        public void StartViewing()
        {
            _client.ScreenDataReceived -= OnScreenDataReceived;
            _client.ScreenDataReceived += OnScreenDataReceived;
            _client.ConnectionStateChanged -= OnConnectionStateChanged;
            _client.ConnectionStateChanged += OnConnectionStateChanged;
            _client.ConnectionQualityUpdated -= OnConnectionQualityUpdated;
            _client.ConnectionQualityUpdated += OnConnectionQualityUpdated;
            EnsureFileTransferService();

            _metricsTimer?.Stop();
            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher;
            _metricsTimer = dispatcher.CreateTimer();
            _metricsTimer.Interval = TimeSpan.FromSeconds(1);
            _metricsTimer.Tick += OnMetricsTick;
            _metricsTimer.Start();
            _keyCapture.Focus();
        }

        /// <summary>
        /// Deactivates the viewer: stops the metrics timer and unsubscribes from data events.
        /// </summary>
        public void StopViewing()
        {
            _metricsTimer?.Stop();
            _metricsTimer = null;
            DetachFileTransferService();
            HideDropOverlay();
            RestoreViewerCursor();
            _client.ScreenDataReceived -= OnScreenDataReceived;
            _client.ConnectionStateChanged -= OnConnectionStateChanged;
            _client.ConnectionQualityUpdated -= OnConnectionQualityUpdated;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            StartViewing();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopViewing();
        }

    // ── Frame rendering ──────────────────────────────────────────────────────

    private void OnScreenDataReceived(object? sender, ScreenData screenData)
    {
        try
        {
            var snapshot = RemoteFrameSnapshotService.CreateSnapshot(screenData);
            if (snapshot is null)
                return;

            _latestSnapshot = snapshot;

            if (screenData.Width > 0) _remoteWidth = screenData.Width;
            if (screenData.Height > 0) _remoteHeight = screenData.Height;

            Interlocked.Increment(ref _frameCount);
            TryRenderLatestSnapshot();
        }
        catch
        {
        }
    }

    private void TryRenderLatestSnapshot()
    {
        if (_frameRenderBusy)
            return;

        _frameRenderBusy = true;
        MainThread.BeginInvokeOnMainThread(RenderLatestSnapshot);
    }

    private void RenderLatestSnapshot()
    {
        try
        {
            var snapshot = _latestSnapshot;
            if (snapshot is null)
                return;

            // Hand the encoded bytes directly to the GPU handler.  The handler decodes
            // asynchronously and presents to the swap chain — no XAML image pipeline.
            _gpuViewer.RenderFrame(snapshot.ImageBytes);

            if (_remoteWidth > 0 && _remoteHeight > 0)
                _resolutionLabel.Text = $"Resolution: {_remoteWidth}x{_remoteHeight}";
        }
        catch (Exception)
        {
            // Prevent rendering failures from propagating to the UI dispatcher
            // as unhandled exceptions.  The next frame will retry.
        }
        finally
        {
            _frameRenderBusy = false;
        }
    }

    private async void OnScreenshotClicked(object? sender, EventArgs e)
    {
        var snapshot = _latestSnapshot;
        if (snapshot is null)
        {
            await DisplayAlertAsync("Screenshot", "Wait for the remote desktop to render a frame, then try again.", "OK");
            return;
        }

        try
        {
            var picturesDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrWhiteSpace(picturesDirectory))
                picturesDirectory = FileSystem.Current.AppDataDirectory;

            var targetDirectory = Path.Combine(picturesDirectory, "RemoteLink", "Screenshots");
            Directory.CreateDirectory(targetDirectory);

            var fileName = RemoteFrameSnapshotService.BuildFileName(_client.ConnectedHost?.DeviceName, snapshot);
            var path = GetUniquePath(Path.Combine(targetDirectory, fileName));
            await File.WriteAllBytesAsync(path, snapshot.ImageBytes);

            UpdateTransferStatus($"Saved screenshot: {Path.GetFileName(path)}", ThemeColors.Success);

            var choice = await DisplayActionSheetAsync(
                "Screenshot Saved",
                "OK",
                null,
                "Open File",
                "Open Directory");

            if (choice == "Open File")
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            else if (choice == "Open Directory")
            {
                var directory = Path.GetDirectoryName(path) ?? targetDirectory;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save remote screenshot");
            await DisplayAlertAsync("Screenshot", $"Failed to save screenshot: {ex.Message}", "OK");
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

    private void OnConnectionQualityUpdated(object? sender, ConnectionQuality quality)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _latencyLabel.Text = $"Latency: {quality.Latency} ms";
        });
    }

    // ── Mouse input forwarding ───────────────────────────────────────────────

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_client.IsConnected || _remoteWidth <= 0 || _remoteHeight <= 0) return;

        var position = e.GetPosition(_gpuViewer);
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

        var position = e.GetPosition(_gpuViewer);
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

        var position = e.GetPosition(_gpuViewer);
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
        double viewerWidth = _gpuViewer.Width;
        double viewerHeight = _gpuViewer.Height;

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
        if (state == ClientConnectionState.Connected)
            EnsureFileTransferService();
        else if (state == ClientConnectionState.Disconnected)
        {
            DetachFileTransferService();
            RemoteFrameSnapshotService.ResetFrameCache();
        }

        if (state == ClientConnectionState.Disconnected && !_isDisconnecting)
        {
            if (_client.IsAutoReconnectPending)
                return;

            if (_closeSessionAsync is not null)
                return;

            // Remote host dropped the connection — navigate back
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await this.DisplayAlertAsync("Disconnected", "The remote host has ended the session.", "OK");
                await Navigation.PopAsync();
            });
        }
    }

    private void EnsureFileTransferService()
    {
        var communicationService = _client.CurrentCommunicationService;
        if (!_client.IsConnected || communicationService is null)
            return;

        if (ReferenceEquals(_boundCommunicationService, communicationService) && _fileTransferService is not null)
            return;

        DetachFileTransferService();

        _boundCommunicationService = communicationService;
        _fileTransferService = new FileTransferService(NullLogger<FileTransferService>.Instance, communicationService);
        _fileTransferService.TransferResponseReceived += OnTransferResponseReceived;
        _fileTransferService.ProgressUpdated += OnTransferProgressUpdated;
        _fileTransferService.TransferCompleted += OnTransferCompleted;
    }

    private void DetachFileTransferService()
    {
        if (_fileTransferService is not null)
        {
            _fileTransferService.TransferResponseReceived -= OnTransferResponseReceived;
            _fileTransferService.ProgressUpdated -= OnTransferProgressUpdated;
            _fileTransferService.TransferCompleted -= OnTransferCompleted;
        }

        _fileTransferService = null;
        _boundCommunicationService = null;
        _pendingTransferNames.Clear();
    }

    private void OnTransferResponseReceived(object? sender, FileTransferResponse response)
    {
        var fileName = _pendingTransferNames.GetValueOrDefault(response.TransferId, "file");
        UpdateTransferStatus(
            response.Accepted
                ? $"Remote host accepted {fileName}"
                : $"Transfer rejected for {fileName}",
            response.Accepted ? ThemeColors.Warning : ThemeColors.Danger);
    }

    private void OnTransferProgressUpdated(object? sender, FileTransferProgress progress)
    {
        var fileName = _pendingTransferNames.GetValueOrDefault(progress.TransferId, "file");
        UpdateTransferStatus(
            $"Sending {fileName} — {progress.PercentComplete:0}%",
            ThemeColors.Warning);
    }

    private void OnTransferCompleted(object? sender, FileTransferComplete complete)
    {
        var fileName = _pendingTransferNames.TryRemove(complete.TransferId, out var name)
            ? name
            : "file";

        UpdateTransferStatus(
            complete.Success
                ? $"Sent {fileName}"
                : $"Transfer failed for {fileName}",
            complete.Success ? ThemeColors.Success : ThemeColors.Danger);
    }

    private void UpdateTransferStatus(string status, Color color)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _transferStatusLabel.Text = status;
            _transferStatusLabel.TextColor = color;
        });
    }

    private void ShowDropOverlay()
    {
        MainThread.BeginInvokeOnMainThread(() => _dropOverlay.IsVisible = true);
    }

    private void HideDropOverlay()
    {
        MainThread.BeginInvokeOnMainThread(() => _dropOverlay.IsVisible = false);
    }

    private async Task StartDroppedFilesTransferAsync(IReadOnlyList<string> filePaths)
    {
        if (!_client.IsConnected)
        {
            UpdateTransferStatus("Connect to a host before sending files", ThemeColors.Danger);
            return;
        }

        if (_client.CurrentSessionPermissions?.AllowFileTransfer == false)
        {
            UpdateTransferStatus("File transfer is disabled for this session", ThemeColors.Danger);
            return;
        }

        EnsureFileTransferService();
        if (_fileTransferService is null)
        {
            UpdateTransferStatus("File transfer is unavailable for this connection", ThemeColors.Danger);
            return;
        }

        var queuedCount = 0;

        foreach (var path in filePaths.Where(File.Exists))
        {
            try
            {
                var fileName = Path.GetFileName(path);
                var transferId = await _fileTransferService.InitiateTransferAsync(path, FileTransferDirection.Upload);
                _pendingTransferNames[transferId] = fileName;
                queuedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to queue dropped file {Path}", path);
            }
        }

        if (queuedCount > 0)
        {
            var targetName = _client.ConnectedHost?.DeviceName ?? "remote host";
            UpdateTransferStatus(
                queuedCount == 1
                    ? $"Queued 1 file for {targetName}"
                    : $"Queued {queuedCount} files for {targetName}",
                ThemeColors.Warning);
        }
        else
        {
            UpdateTransferStatus("Only local files can be dropped into the viewer", ThemeColors.Danger);
        }
    }

#if WINDOWS
    private void OnRemoteViewerHandlerChanged(object? sender, EventArgs e)
    {
        if (_nativeDropTarget is not null)
        {
            _nativeDropTarget.DragOver -= OnNativeViewerDragOver;
            _nativeDropTarget.DragLeave -= OnNativeViewerDragLeave;
            _nativeDropTarget.Drop -= OnNativeViewerDrop;
            _nativeDropTarget.PointerEntered -= OnNativeViewerPointerEntered;
            _nativeDropTarget.PointerExited -= OnNativeViewerPointerExited;
        }

        _nativeDropTarget = _gpuViewer.Handler?.PlatformView as WinFrameworkElement;
        if (_nativeDropTarget is null)
            return;

        _nativeDropTarget.AllowDrop = true;
        _nativeDropTarget.DragOver += OnNativeViewerDragOver;
        _nativeDropTarget.DragLeave += OnNativeViewerDragLeave;
        _nativeDropTarget.Drop += OnNativeViewerDrop;
        _nativeDropTarget.PointerEntered += OnNativeViewerPointerEntered;
        _nativeDropTarget.PointerExited += OnNativeViewerPointerExited;
    }

    private void OnNativeViewerPointerEntered(object sender, WinPointerRoutedEventArgs e)
        => HideViewerCursor();

    private void OnNativeViewerPointerExited(object sender, WinPointerRoutedEventArgs e)
        => RestoreViewerCursor();

    [DllImport("user32.dll")]
    private static extern int ShowCursor(bool bShow);

    private void OnNativeViewerDragOver(object sender, WinDragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = WinDataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Send files to the remote host";
            e.DragUIOverride.IsCaptionVisible = true;
            ShowDropOverlay();
            return;
        }

        e.AcceptedOperation = WinDataPackageOperation.None;
        HideDropOverlay();
    }

    private void OnNativeViewerDragLeave(object sender, WinDragEventArgs e)
    {
        HideDropOverlay();
    }

    private async void OnNativeViewerDrop(object sender, WinDragEventArgs e)
    {
        HideDropOverlay();

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        var storageItems = await e.DataView.GetStorageItemsAsync();
        var filePaths = storageItems
            .Where(item => item.IsOfType(StorageItemTypes.File) && !string.IsNullOrWhiteSpace(item.Path))
            .Select(item => item.Path)
            .Where(File.Exists)
            .ToList();

        await StartDroppedFilesTransferAsync(filePaths);
    }
    private void HideViewerCursor()
    {
        if (!_viewerCursorHidden)
        {
            ShowCursor(false);
            _viewerCursorHidden = true;
        }
    }

    private void RestoreViewerCursor()
    {
        if (_viewerCursorHidden)
        {
            ShowCursor(true);
            _viewerCursorHidden = false;
        }
    }
#else
    private void OnRemoteViewerHandlerChanged(object? sender, EventArgs e)
    {
    }

    private void HideViewerCursor() { }
    private void RestoreViewerCursor() { }
#endif

    private async void OnDisconnectClicked(object? sender, EventArgs e)
    {
        _isDisconnecting = true;
        _latestSnapshot = null;

        try
        {
            if (_closeSessionAsync is not null)
            {
                await _closeSessionAsync();
                return;
            }

            await _client.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disconnect");
        }

        if (_closeSessionAsync is null)
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

    private async void OnSystemInfoClicked(object? sender, EventArgs e)
    {
        if (!_client.IsConnected)
            return;

        try
        {
            var systemInfo = await _client.GetRemoteSystemInfoAsync();
            var formatted = FormatSystemInfo(systemInfo);
            var copy = await DisplayAlertAsync("Remote System Info", formatted, "Copy", "Close");
            if (copy)
                await Clipboard.SetTextAsync(formatted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve remote system information");
            await DisplayAlertAsync("Remote System Info", $"Failed to load remote system information: {ex.Message}", "OK");
        }
    }

    private async void OnRemoteCommandClicked(object? sender, EventArgs e)
    {
        if (!_client.IsConnected)
            return;

        var shellLabel = await DisplayActionSheetAsync(
            "Remote Command",
            "Cancel",
            null,
            "PowerShell",
            "Command Prompt");

        if (string.IsNullOrWhiteSpace(shellLabel) || shellLabel == "Cancel")
            return;

        var shell = shellLabel == "Command Prompt"
            ? RemoteCommandShell.CommandPrompt
            : RemoteCommandShell.PowerShell;

        var commandText = await DisplayPromptAsync(
            "Remote Command",
            $"Enter a {shellLabel} command or script path to run on {_client.ConnectedHost?.DeviceName ?? "the remote host"}.",
            accept: "Run",
            cancel: "Cancel",
            placeholder: shell == RemoteCommandShell.PowerShell ? "Get-Process | Select-Object -First 10" : "whoami",
            maxLength: 4000,
            keyboard: Keyboard.Text);

        if (string.IsNullOrWhiteSpace(commandText))
            return;

        try
        {
            var result = await _client.ExecuteRemoteCommandAsync(commandText, shell);
            var formatted = FormatCommandResult(result);
            var copy = await DisplayAlertAsync("Remote Command", formatted, "Copy", "Close");
            if (copy)
                await Clipboard.SetTextAsync(formatted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute remote command");
            await DisplayAlertAsync("Remote Command", $"Failed to execute remote command: {ex.Message}", "OK");
        }
    }

    private static string FormatSystemInfo(RemoteSystemInfo info)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Machine: {ValueOrUnknown(info.MachineName)}");
        builder.AppendLine($"OS: {ValueOrUnknown(info.OperatingSystem)} ({ValueOrUnknown(info.OsArchitecture)})");
        builder.AppendLine($"Runtime: {ValueOrUnknown(info.FrameworkDescription)}");
        builder.AppendLine($"CPU: {ValueOrUnknown(info.ProcessorName)}");
        builder.AppendLine($"Logical processors: {info.LogicalProcessorCount}");
        builder.AppendLine($"Memory: {FormatBytes(info.AvailableMemoryBytes)} free / {FormatBytes(info.TotalMemoryBytes)} total");
        builder.AppendLine($"Uptime: {FormatDuration(info.UptimeSeconds)}");

        if (info.Disks.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Disks:");
            foreach (var disk in info.Disks.Take(3))
            {
                builder.AppendLine($"• {ValueOrUnknown(disk.Name)} — {FormatBytes(disk.AvailableFreeSpaceBytes)} free / {FormatBytes(disk.TotalSizeBytes)}");
            }
        }

        if (info.NetworkInterfaces.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Network:");
            foreach (var network in info.NetworkInterfaces.Take(3))
            {
                var address = network.IPv4Addresses.FirstOrDefault()
                    ?? network.IPv6Addresses.FirstOrDefault()
                    ?? "No IP";
                builder.AppendLine($"• {ValueOrUnknown(network.Name)} ({network.OperationalStatus}) — {address}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string GetUniquePath(string fullPath)
    {
        if (!File.Exists(fullPath))
            return fullPath;

        var directory = Path.GetDirectoryName(fullPath) ?? FileSystem.Current.AppDataDirectory;
        var fileName = Path.GetFileNameWithoutExtension(fullPath);
        var extension = Path.GetExtension(fullPath);

        for (var counter = 2; counter < 10_000; counter++)
        {
            var candidate = Path.Combine(directory, $"{fileName}_{counter}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(directory, $"{fileName}_{Guid.NewGuid():N}{extension}");
    }

    private static string FormatCommandResult(RemoteCommandExecutionResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Shell: {result.Shell}");
        builder.AppendLine($"Status: {(result.TimedOut ? "Timed out" : result.Succeeded ? "Succeeded" : "Failed")}");
        builder.AppendLine($"Exit code: {result.ExitCode}");
        builder.AppendLine($"Duration: {result.DurationMs} ms");

        if (!string.IsNullOrWhiteSpace(result.WorkingDirectory))
            builder.AppendLine($"Working directory: {result.WorkingDirectory}");

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            builder.AppendLine();
            builder.AppendLine("Output:");
            builder.AppendLine(TrimCommandOutput(result.StandardOutput));
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            builder.AppendLine();
            builder.AppendLine("Errors:");
            builder.AppendLine(TrimCommandOutput(result.StandardError));
        }

        return builder.ToString().TrimEnd();
    }

    private static string TrimCommandOutput(string value)
    {
        const int maxLength = 1600;
        if (value.Length <= maxLength)
            return value;

        return value[..maxLength] + Environment.NewLine + "...output truncated...";
    }

    private static string ValueOrUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Unknown" : value;

    private static string FormatDuration(long uptimeSeconds)
    {
        if (uptimeSeconds <= 0)
            return "Unknown";

        var uptime = TimeSpan.FromSeconds(uptimeSeconds);
        return uptime.TotalDays >= 1
            ? $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m"
            : uptime.TotalHours >= 1
                ? $"{(int)uptime.TotalHours}h {uptime.Minutes}m"
                : $"{uptime.Minutes}m {uptime.Seconds}s";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
            return "0 B";

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{value:0} {units[unitIndex]}" : $"{value:0.0} {units[unitIndex]}";
    }

    private static View CreateGridChild(View view, int column = 0, int row = 0)
    {
        Grid.SetColumn(view, column);
        Grid.SetRow(view, row);
        return view;
    }
}
