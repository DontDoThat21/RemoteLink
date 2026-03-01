using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using RemoteLink.Desktop.Services;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

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

    private CancellationTokenSource? _hostCts;
    private IDispatcherTimer? _pinExpiryTimer;

    // UI state
    private string _currentPin = "------";
    private string _statusText = "Stopped";
    private Color _statusColor = Colors.Gray;
    private bool _isRunning;
    private string _connectionInfo = "No active connections";
    private bool _pinVisible;
    private string _deviceNumericId;

    // UI element references
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

    public MainPage(
        ILogger<MainPage> logger,
        RemoteDesktopHost host,
        IPairingService pairing,
        ICommunicationService communication,
        INetworkDiscovery networkDiscovery,
        IInputHandler inputHandler,
        IPerformanceMonitor perfMonitor)
    {
        _logger = logger;
        _host = host;
        _pairing = pairing;
        _communication = communication;
        _networkDiscovery = networkDiscovery;
        _inputHandler = inputHandler;
        _perfMonitor = perfMonitor;

        _deviceNumericId = GenerateNumericId(Environment.MachineName);

        Title = "RemoteLink Desktop";
        BackgroundColor = Color.FromArgb("#F5F5F5");

        Content = BuildLayout();

        _communication.ConnectionStateChanged += OnConnectionStateChanged;
        _communication.PairingRequestReceived += OnPairingRequestReceivedUI;
    }

    /// <summary>
    /// Generates a stable 9-digit numeric ID from the machine name,
    /// similar to TeamViewer's device IDs.
    /// </summary>
    private static string GenerateNumericId(string machineName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(machineName + "RemoteLink"));
        // Take first 8 bytes and reduce to a 9-digit number (100000000–999999999)
        long value = Math.Abs(BitConverter.ToInt64(hash, 0));
        long id = (value % 900_000_000) + 100_000_000;
        // Format as "XXX XXX XXX" for readability
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
                new RowDefinition(GridLength.Auto),   // Allow Remote Control panel
                new RowDefinition(GridLength.Star),   // Connection status area
                new RowDefinition(GridLength.Auto),   // Start/Stop button
                new RowDefinition(GridLength.Auto),   // Status bar
            },
            Padding = new Thickness(0),
            RowSpacing = 0
        };

        root.Add(BuildHeader(), 0, 0);
        root.Add(BuildRemoteControlPanel(), 0, 1);
        root.Add(BuildConnectionPanel(), 0, 2);
        root.Add(BuildControlPanel(), 0, 3);
        root.Add(BuildStatusBar(), 0, 4);

        return root;
    }

    private View BuildHeader()
    {
        return new Grid
        {
            BackgroundColor = Color.FromArgb("#512BD4"),
            Padding = new Thickness(20, 16),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
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
                    TextColor = Color.FromArgb("#D0C0FF"),
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.End
                }, column: 1)
            }
        };
    }

    /// <summary>
    /// Builds the combined "Allow Remote Control" panel — the central TeamViewer-style
    /// card displaying both "Your ID" and the connection PIN prominently.
    /// </summary>
    private View BuildRemoteControlPanel()
    {
        // ── Your ID row ──
        _deviceIdLabel = new Label
        {
            Text = _deviceNumericId,
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#222222"),
            VerticalOptions = LayoutOptions.Center,
            FontFamily = "OpenSansRegular"
        };

        _copyIdFeedback = new Label
        {
            Text = "",
            FontSize = 10,
            TextColor = Color.FromArgb("#4CAF50"),
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false
        };

        var copyIdButton = new Button
        {
            Text = "Copy",
            FontSize = 11,
            BackgroundColor = Color.FromArgb("#E8E0FF"),
            TextColor = Color.FromArgb("#512BD4"),
            CornerRadius = 4,
            Padding = new Thickness(10, 2),
            HeightRequest = 28,
            VerticalOptions = LayoutOptions.Center
        };
        copyIdButton.Clicked += OnCopyIdClicked;

        var idValueRow = new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            Children = { _deviceIdLabel, copyIdButton, _copyIdFeedback }
        };

        // ── PIN row ──
        _pinLabel = new Label
        {
            Text = FormatPinDisplay(),
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#512BD4"),
            VerticalOptions = LayoutOptions.Center,
            CharacterSpacing = 4,
            FontFamily = "OpenSansRegular"
        };

        _pinVisibilityButton = new Button
        {
            Text = "Show",
            FontSize = 11,
            BackgroundColor = Color.FromArgb("#E8E0FF"),
            TextColor = Color.FromArgb("#512BD4"),
            CornerRadius = 4,
            Padding = new Thickness(10, 2),
            HeightRequest = 28,
            VerticalOptions = LayoutOptions.Center
        };
        _pinVisibilityButton.Clicked += OnTogglePinVisibility;

        _copyPinFeedback = new Label
        {
            Text = "",
            FontSize = 10,
            TextColor = Color.FromArgb("#4CAF50"),
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false
        };

        var copyPinButton = new Button
        {
            Text = "Copy",
            FontSize = 11,
            BackgroundColor = Color.FromArgb("#E8E0FF"),
            TextColor = Color.FromArgb("#512BD4"),
            CornerRadius = 4,
            Padding = new Thickness(10, 2),
            HeightRequest = 28,
            VerticalOptions = LayoutOptions.Center
        };
        copyPinButton.Clicked += OnCopyPinClicked;

        var pinValueRow = new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            Children = { _pinLabel, _pinVisibilityButton, copyPinButton, _copyPinFeedback }
        };

        // ── PIN metadata row (expiry + attempts) ──
        _pinExpiryLabel = new Label
        {
            Text = "",
            FontSize = 11,
            TextColor = Color.FromArgb("#999999"),
            HorizontalOptions = LayoutOptions.Center
        };

        _attemptsLabel = new Label
        {
            Text = "",
            FontSize = 11,
            TextColor = Color.FromArgb("#999999"),
            HorizontalOptions = LayoutOptions.Center
        };

        var pinMetaRow = new HorizontalStackLayout
        {
            Spacing = 16,
            HorizontalOptions = LayoutOptions.Center,
            Children = { _pinExpiryLabel, _attemptsLabel }
        };

        // ── Refresh PIN button ──
        var refreshButton = new Button
        {
            Text = "Refresh PIN",
            FontSize = 12,
            BackgroundColor = Color.FromArgb("#E8E0FF"),
            TextColor = Color.FromArgb("#512BD4"),
            CornerRadius = 4,
            Padding = new Thickness(14, 4),
            HeightRequest = 32,
            HorizontalOptions = LayoutOptions.Center
        };
        refreshButton.Clicked += OnRefreshPinClicked;

        // ── Divider ──
        var divider = new BoxView
        {
            Color = Color.FromArgb("#E8E0FF"),
            HeightRequest = 1,
            Margin = new Thickness(0, 8, 0, 8)
        };

        // ── Assemble the card ──
        return new Border
        {
            Margin = new Thickness(16, 12, 16, 4),
            Padding = new Thickness(20, 16),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#D8D0F0"),
            StrokeThickness = 1,
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(Color.FromArgb("#20000000")),
                Offset = new Point(0, 2),
                Radius = 6
            },
            Content = new StackLayout
            {
                Spacing = 4,
                Children =
                {
                    // Panel title
                    new Label
                    {
                        Text = "Allow Remote Control",
                        FontSize = 15,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#512BD4"),
                        HorizontalOptions = LayoutOptions.Center,
                        Margin = new Thickness(0, 0, 0, 8)
                    },

                    // Your ID section
                    new Label
                    {
                        Text = "Your ID",
                        FontSize = 12,
                        TextColor = Color.FromArgb("#888888"),
                        HorizontalOptions = LayoutOptions.Center
                    },
                    idValueRow,

                    divider,

                    // PIN section
                    new Label
                    {
                        Text = "Password",
                        FontSize = 12,
                        TextColor = Color.FromArgb("#888888"),
                        HorizontalOptions = LayoutOptions.Center
                    },
                    pinValueRow,
                    pinMetaRow,
                    refreshButton,
                }
            }
        };
    }

    private View BuildConnectionPanel()
    {
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
            TextColor = Color.FromArgb("#666666"),
            VerticalOptions = LayoutOptions.Center
        };

        return new Border
        {
            Margin = new Thickness(16, 4, 16, 4),
            Padding = new Thickness(16),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E0E0E0"),
            StrokeThickness = 1,
            VerticalOptions = LayoutOptions.Fill,
            Content = new StackLayout
            {
                Spacing = 12,
                Children =
                {
                    new Label
                    {
                        Text = "Connection Status",
                        FontSize = 12,
                        TextColor = Color.FromArgb("#888888"),
                    },
                    new HorizontalStackLayout
                    {
                        Spacing = 8,
                        Children = { _statusIndicator, _connectionLabel }
                    },
                    new Label
                    {
                        Text = "Waiting for incoming connections...\nMobile clients on the same network will discover this host automatically.",
                        FontSize = 12,
                        TextColor = Color.FromArgb("#AAAAAA"),
                        LineBreakMode = LineBreakMode.WordWrap
                    }
                }
            }
        };
    }

    private View BuildControlPanel()
    {
        _startStopButton = new Button
        {
            Text = "Start Host",
            FontSize = 16,
            BackgroundColor = Color.FromArgb("#512BD4"),
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
            BackgroundColor = Color.FromArgb("#333333"),
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
                    TextColor = Color.FromArgb("#999999"),
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

        // Format as "XXX XXX" for readability
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
                if (_pinExpiryLabel != null)
                {
                    _pinExpiryLabel.Text = "";
                    _pinExpiryLabel.IsVisible = false;
                }
                if (_attemptsLabel != null)
                {
                    _attemptsLabel.Text = "";
                    _attemptsLabel.IsVisible = false;
                }
                return;
            }

            // Expiry status
            if (_pinExpiryLabel != null)
            {
                _pinExpiryLabel.IsVisible = true;
                if (_pairing.IsPinExpired)
                {
                    _pinExpiryLabel.Text = "PIN expired";
                    _pinExpiryLabel.TextColor = Color.FromArgb("#D32F2F");
                }
                else
                {
                    _pinExpiryLabel.Text = "PIN active";
                    _pinExpiryLabel.TextColor = Color.FromArgb("#4CAF50");
                }
            }

            // Attempts remaining
            if (_attemptsLabel != null)
            {
                _attemptsLabel.IsVisible = true;
                if (_pairing.IsLockedOut)
                {
                    _attemptsLabel.Text = "Locked out";
                    _attemptsLabel.TextColor = Color.FromArgb("#D32F2F");
                }
                else
                {
                    int remaining = _pairing.AttemptsRemaining;
                    _attemptsLabel.Text = $"{remaining} attempts left";
                    _attemptsLabel.TextColor = remaining <= 2
                        ? Color.FromArgb("#FFA500")
                        : Color.FromArgb("#999999");
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

    // ── Event Handlers ─────────────────────────────────────────────────

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
            UpdateStatusBar("Starting...", Color.FromArgb("#FFA500"));

            // Generate PIN and update display
            _currentPin = _pairing.GeneratePin();
            _pinVisible = false;
            RefreshPinDisplay();
            StartPinExpiryTimer();

            // Start the host background service
            _hostCts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try
                {
                    await _host.StartAsync(_hostCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected on stop
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Host service error");
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        UpdateStatusBar("Error: " + ex.Message, Colors.Red);
                    });
                }
            });

            UpdateStatusBar("Running — Listening for connections", Color.FromArgb("#4CAF50"));

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_startStopButton != null)
                {
                    _startStopButton.Text = "Stop Host";
                    _startStopButton.BackgroundColor = Color.FromArgb("#D32F2F");
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
            UpdateStatusBar("Stopping...", Color.FromArgb("#FFA500"));

            _hostCts?.Cancel();
            await _host.StopAsync(CancellationToken.None);

            _isRunning = false;
            _currentPin = "------";
            _pinVisible = false;
            RefreshPinDisplay();
            StopPinExpiryTimer();
            UpdatePinMetadata();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_startStopButton != null)
                {
                    _startStopButton.Text = "Start Host";
                    _startStopButton.BackgroundColor = Color.FromArgb("#512BD4");
                }
            });

            UpdateStatusBar("Stopped", Colors.Gray);
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
        // Copy the raw digits (no spaces) to clipboard
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
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (connected)
            {
                _connectionInfo = "Client connected — awaiting PIN pairing";
                if (_statusIndicator != null)
                    _statusIndicator.Color = Color.FromArgb("#FFA500");
                if (_connectionLabel != null)
                    _connectionLabel.Text = _connectionInfo;
            }
            else
            {
                _connectionInfo = "No active connections";
                if (_statusIndicator != null)
                    _statusIndicator.Color = _isRunning ? Color.FromArgb("#4CAF50") : Colors.Gray;
                if (_connectionLabel != null)
                    _connectionLabel.Text = _connectionInfo;

                // Refresh PIN metadata (new PIN generated by host on disconnect)
                UpdatePinMetadata();
            }
        });
    }

    private void OnPairingRequestReceivedUI(object? sender, PairingRequest request)
    {
        // Update attempts display after each pairing attempt
        UpdatePinMetadata();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_connectionLabel != null)
                _connectionLabel.Text = $"Pairing attempt from {request.ClientDeviceName}...";
        });
    }

    private void UpdateStatusBar(string status, Color color)
    {
        _statusText = status;
        _statusColor = color;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_statusLabel != null)
                _statusLabel.Text = _statusText;
            if (_statusIndicator != null && !_statusText.Contains("connection"))
                _statusIndicator.Color = _statusColor;
        });
    }

    // ── Helpers ─────────────────────────────────────────────────────────

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
