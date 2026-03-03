using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using DeviceInfo = RemoteLink.Shared.Models.DeviceInfo;
using DeviceType = RemoteLink.Shared.Models.DeviceType;

namespace RemoteLink.Mobile;

/// <summary>
/// Devices tab: shows discovered desktop hosts on the local network.
/// Each device is displayed as a card with name, IP, and status.
/// Tapping a device navigates to the Connect tab with a PIN prompt.
/// </summary>
public class DevicesPage : ContentPage
{
    private readonly ILogger<DevicesPage> _logger;
    private readonly RemoteDesktopClient _client;
    private readonly List<DeviceInfo> _devices = new();

    private StackLayout _deviceListLayout = null!;
    private Label _emptyLabel = null!;
    private Label _countLabel = null!;

    public DevicesPage(ILogger<DevicesPage> logger, RemoteDesktopClient client)
    {
        _logger = logger;
        _client = client;

        Title = "Devices";
        BackgroundColor = Colors.White;

        Content = BuildLayout();

        _client.DeviceDiscovered += OnDeviceDiscovered;
        _client.DeviceLost += OnDeviceLost;
        _client.ConnectionStateChanged += OnConnectionStateChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshDeviceList();
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
            Text = "Nearby Devices",
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#512BD4"),
            Margin = new Thickness(0, 8, 0, 0)
        });

        _countLabel = new Label
        {
            Text = "Scanning for devices on your network...",
            FontSize = 13,
            TextColor = Colors.Gray,
            Margin = new Thickness(0, 0, 0, 8)
        };
        root.Add(_countLabel);

        // Separator
        root.Add(new BoxView
        {
            Color = Color.FromArgb("#E0E0E0"),
            HeightRequest = 1,
            HorizontalOptions = LayoutOptions.Fill
        });

        // Connection status banner
        var connectionBanner = BuildConnectionBanner();
        root.Add(connectionBanner);

        // Device list
        _emptyLabel = new Label
        {
            Text = "No devices found yet.\nMake sure RemoteLink Desktop is running on the same network.",
            FontSize = 14,
            TextColor = Colors.Gray,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 40, 0, 0)
        };

        _deviceListLayout = new StackLayout { Spacing = 10 };
        _deviceListLayout.Add(_emptyLabel);

        var scrollView = new ScrollView
        {
            Content = _deviceListLayout,
            VerticalOptions = LayoutOptions.Fill
        };
        root.Add(scrollView);

        return root;
    }

    private View BuildConnectionBanner()
    {
        var banner = new Border
        {
            BackgroundColor = Color.FromArgb("#E8F5E9"),
            Stroke = Color.FromArgb("#4CAF50"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(12),
            IsVisible = false,
            AutomationId = "connection-banner"
        };

        var label = new Label
        {
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#2E7D32"),
            AutomationId = "connection-label"
        };

        banner.Content = label;

        // Update visibility based on connection state
        _client.ConnectionStateChanged += (_, state) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                banner.IsVisible = state == ClientConnectionState.Connected;
                if (state == ClientConnectionState.Connected)
                    label.Text = $"Connected to {_client.ConnectedHost?.DeviceName ?? "Unknown"}";
            });
        };

        if (_client.IsConnected)
        {
            banner.IsVisible = true;
            label.Text = $"Connected to {_client.ConnectedHost?.DeviceName ?? "Unknown"}";
        }

        return banner;
    }

    // ── Device list management ─────────────────────────────────────────

    private void RefreshDeviceList()
    {
        // Sync the device list layout with the current known devices
        _deviceListLayout.Clear();

        if (_devices.Count == 0)
        {
            _deviceListLayout.Add(_emptyLabel);
            _countLabel.Text = "Scanning for devices on your network...";
            return;
        }

        _countLabel.Text = $"{_devices.Count} device(s) found";

        foreach (var device in _devices)
            _deviceListLayout.Add(BuildDeviceCard(device));
    }

    private View BuildDeviceCard(DeviceInfo device)
    {
        var isConnected = _client.IsConnected && _client.ConnectedHost?.DeviceId == device.DeviceId;

        var card = new Border
        {
            BackgroundColor = isConnected ? Color.FromArgb("#F3E8FF") : Colors.White,
            Stroke = isConnected ? Color.FromArgb("#512BD4") : Color.FromArgb("#DADCE0"),
            StrokeThickness = isConnected ? 2 : 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14),
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(Color.FromArgb("#20000000")),
                Offset = new Point(0, 2),
                Radius = 6
            }
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

        // Device icon
        var icon = new Label
        {
            Text = device.Type == DeviceType.Desktop ? "\ud83d\udda5" : "\ud83d\udcf1",
            FontSize = 28,
            VerticalOptions = LayoutOptions.Center
        };
        Grid.SetColumn(icon, 0);

        // Device info
        var infoStack = new StackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        infoStack.Add(new Label
        {
            Text = device.DeviceName,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.Black
        });
        infoStack.Add(new Label
        {
            Text = $"{device.IPAddress}:{device.Port}",
            FontSize = 12,
            TextColor = Colors.Gray
        });
        if (isConnected)
        {
            infoStack.Add(new Label
            {
                Text = "Currently connected",
                FontSize = 11,
                TextColor = Color.FromArgb("#512BD4"),
                FontAttributes = FontAttributes.Italic
            });
        }
        Grid.SetColumn(infoStack, 1);

        // Connect chevron
        var chevron = new Label
        {
            Text = isConnected ? "Connected" : "Connect >",
            FontSize = 13,
            TextColor = isConnected ? Color.FromArgb("#512BD4") : Color.FromArgb("#999999"),
            VerticalOptions = LayoutOptions.Center
        };
        Grid.SetColumn(chevron, 2);

        grid.Add(icon);
        grid.Add(infoStack);
        grid.Add(chevron);
        card.Content = grid;

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OnDeviceTappedAsync(device);
        card.GestureRecognizers.Add(tap);

        return card;
    }

    private async Task OnDeviceTappedAsync(DeviceInfo device)
    {
        if (_client.IsConnected && _client.ConnectedHost?.DeviceId == device.DeviceId)
        {
            // Already connected — switch to Connect tab to see the viewer
            Shell.Current.CurrentItem = Shell.Current.Items[0]; // first tab = Connect
            return;
        }

        var pin = await DisplayPromptAsync(
            title: $"Connect to {device.DeviceName}",
            message: "Enter the 6-digit PIN shown on the desktop host:",
            accept: "Connect",
            cancel: "Cancel",
            placeholder: "123456",
            maxLength: 6,
            keyboard: Keyboard.Numeric);

        if (string.IsNullOrWhiteSpace(pin)) return;

        var success = await _client.ConnectToHostAsync(device, pin);

        if (success)
        {
            // Navigate to the Connect tab to see the remote viewer
            Shell.Current.CurrentItem = Shell.Current.Items[0];
        }
        else
        {
            await DisplayAlertAsync("Connection Failed", "Could not connect. Check the PIN and try again.", "OK");
        }
    }

    // ── Event handlers ─────────────────────────────────────────────────

    private void OnDeviceDiscovered(object? sender, DeviceInfo device)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_devices.Any(d => d.DeviceId == device.DeviceId))
            {
                _devices.Add(device);
                RefreshDeviceList();
            }
        });
    }

    private void OnDeviceLost(object? sender, DeviceInfo device)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var existing = _devices.FirstOrDefault(d => d.DeviceId == device.DeviceId);
            if (existing != null)
            {
                _devices.Remove(existing);
                RefreshDeviceList();
            }
        });
    }

    private void OnConnectionStateChanged(object? sender, ClientConnectionState state)
    {
        MainThread.BeginInvokeOnMainThread(RefreshDeviceList);
    }
}
