using System.ComponentModel;
using System.Runtime.CompilerServices;
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

    // UI state
    private string _currentPin = "------";
    private string _statusText = "Stopped";
    private Color _statusColor = Colors.Gray;
    private bool _isRunning;
    private string _connectionInfo = "No active connections";

    // UI element references for updating
    private Label? _pinLabel;
    private Label? _statusLabel;
    private Label? _connectionLabel;
    private Button? _startStopButton;
    private Label? _deviceIdLabel;
    private BoxView? _statusIndicator;

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

        Title = "RemoteLink Desktop";
        BackgroundColor = Color.FromArgb("#F5F5F5");

        Content = BuildLayout();

        _communication.ConnectionStateChanged += OnConnectionStateChanged;
    }

    private View BuildLayout()
    {
        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),   // Header
                new RowDefinition(GridLength.Auto),   // Your ID panel
                new RowDefinition(GridLength.Auto),   // PIN panel
                new RowDefinition(GridLength.Star),   // Connection status area
                new RowDefinition(GridLength.Auto),   // Start/Stop button
                new RowDefinition(GridLength.Auto),   // Status bar
            },
            Padding = new Thickness(0),
            RowSpacing = 0
        };

        root.Add(BuildHeader(), 0, 0);
        root.Add(BuildIdPanel(), 0, 1);
        root.Add(BuildPinPanel(), 0, 2);
        root.Add(BuildConnectionPanel(), 0, 3);
        root.Add(BuildControlPanel(), 0, 4);
        root.Add(BuildStatusBar(), 0, 5);

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

    private View BuildIdPanel()
    {
        _deviceIdLabel = new Label
        {
            Text = Environment.MachineName,
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#333333"),
            HorizontalOptions = LayoutOptions.Center
        };

        return new Border
        {
            Margin = new Thickness(16, 12, 16, 4),
            Padding = new Thickness(16, 12),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E0E0E0"),
            StrokeThickness = 1,
            Content = new StackLayout
            {
                Spacing = 4,
                Children =
                {
                    new Label
                    {
                        Text = "Your Device ID",
                        FontSize = 12,
                        TextColor = Color.FromArgb("#888888"),
                        HorizontalOptions = LayoutOptions.Center
                    },
                    _deviceIdLabel
                }
            }
        };
    }

    private View BuildPinPanel()
    {
        _pinLabel = new Label
        {
            Text = _currentPin,
            FontSize = 36,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#512BD4"),
            HorizontalOptions = LayoutOptions.Center,
            CharacterSpacing = 8
        };

        var refreshButton = new Button
        {
            Text = "Refresh PIN",
            FontSize = 12,
            BackgroundColor = Color.FromArgb("#E8E0FF"),
            TextColor = Color.FromArgb("#512BD4"),
            CornerRadius = 4,
            Padding = new Thickness(12, 4),
            HeightRequest = 32,
            HorizontalOptions = LayoutOptions.Center
        };
        refreshButton.Clicked += OnRefreshPinClicked;

        return new Border
        {
            Margin = new Thickness(16, 4, 16, 4),
            Padding = new Thickness(16, 12),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E0E0E0"),
            StrokeThickness = 1,
            Content = new StackLayout
            {
                Spacing = 8,
                Children =
                {
                    new Label
                    {
                        Text = "Connection PIN",
                        FontSize = 12,
                        TextColor = Color.FromArgb("#888888"),
                        HorizontalOptions = LayoutOptions.Center
                    },
                    _pinLabel,
                    new Label
                    {
                        Text = "Share this PIN with the connecting device",
                        FontSize = 11,
                        TextColor = Color.FromArgb("#AAAAAA"),
                        HorizontalOptions = LayoutOptions.Center
                    },
                    refreshButton
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

    // ── Event Handlers ─────────────────────────────────────────────────

    private async void OnStartStopClicked(object? sender, EventArgs e)
    {
        if (_isRunning)
        {
            await StopHostAsync();
        }
        else
        {
            await StartHostAsync();
        }
    }

    private async Task StartHostAsync()
    {
        try
        {
            _isRunning = true;
            UpdateUI("Starting...", Color.FromArgb("#FFA500"));

            // Generate PIN
            _currentPin = _pairing.GeneratePin();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_pinLabel != null)
                    _pinLabel.Text = _currentPin;
            });

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
                        UpdateUI("Error: " + ex.Message, Colors.Red);
                    });
                }
            });

            UpdateUI("Running — Listening for connections", Color.FromArgb("#4CAF50"));

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
            UpdateUI("Failed to start: " + ex.Message, Colors.Red);
            await DisplayAlertAsync("Error", $"Failed to start host: {ex.Message}", "OK");
        }
    }

    private async Task StopHostAsync()
    {
        try
        {
            UpdateUI("Stopping...", Color.FromArgb("#FFA500"));

            _hostCts?.Cancel();
            await _host.StopAsync(CancellationToken.None);

            _isRunning = false;
            _currentPin = "------";

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_pinLabel != null)
                    _pinLabel.Text = _currentPin;
                if (_startStopButton != null)
                {
                    _startStopButton.Text = "Start Host";
                    _startStopButton.BackgroundColor = Color.FromArgb("#512BD4");
                }
            });

            UpdateUI("Stopped", Colors.Gray);
            _logger.LogInformation("Desktop host stopped from UI");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop host");
            UpdateUI("Error stopping: " + ex.Message, Colors.Red);
        }
    }

    private void OnRefreshPinClicked(object? sender, EventArgs e)
    {
        if (!_isRunning) return;

        _currentPin = _pairing.GeneratePin();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_pinLabel != null)
                _pinLabel.Text = _currentPin;
        });

        _logger.LogInformation("PIN refreshed from UI: {Pin}", _currentPin);
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
            }
        });
    }

    private void UpdateUI(string status, Color color)
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
