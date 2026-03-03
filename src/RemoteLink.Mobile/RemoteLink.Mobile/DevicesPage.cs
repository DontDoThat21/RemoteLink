using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using DeviceInfo = RemoteLink.Shared.Models.DeviceInfo;
using DeviceType = RemoteLink.Shared.Models.DeviceType;

namespace RemoteLink.Mobile;

/// <summary>
/// Devices tab: address book of saved devices + live-discovered desktop hosts.
/// Saved devices persist across sessions with friendly names and last-connected timestamps.
/// </summary>
public class DevicesPage : ContentPage
{
    private readonly ILogger<DevicesPage> _logger;
    private readonly RemoteDesktopClient _client;
    private readonly ISavedDevicesService _savedDevices;
    private readonly List<DeviceInfo> _devices = new();

    // UI references — address book
    private StackLayout _savedDeviceListLayout = null!;
    private Label _savedEmptyLabel = null!;

    // UI references — discovered
    private StackLayout _deviceListLayout = null!;
    private Label _emptyLabel = null!;
    private Label _countLabel = null!;

    private bool _loaded;

    public DevicesPage(ILogger<DevicesPage> logger, RemoteDesktopClient client, ISavedDevicesService savedDevices)
    {
        _logger = logger;
        _client = client;
        _savedDevices = savedDevices;

        Title = "Devices";
        BackgroundColor = Colors.White;

        Content = BuildLayout();

        _client.DeviceDiscovered += OnDeviceDiscovered;
        _client.DeviceLost += OnDeviceLost;
        _client.ConnectionStateChanged += OnConnectionStateChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_loaded)
        {
            await _savedDevices.LoadAsync();
            _loaded = true;
        }

        RefreshSavedDeviceList();
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
            Text = "Devices",
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#512BD4"),
            Margin = new Thickness(0, 8, 0, 0)
        });

        // Connection status banner
        root.Add(BuildConnectionBanner());

        // ── Address Book section ──
        root.Add(new Label
        {
            Text = "Address Book",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#333333"),
            Margin = new Thickness(0, 4, 0, 0)
        });

        root.Add(new Label
        {
            Text = "Saved devices for quick reconnection",
            FontSize = 12,
            TextColor = Colors.Gray,
            Margin = new Thickness(0, 0, 0, 4)
        });

        _savedEmptyLabel = new Label
        {
            Text = "No saved devices yet.\nConnect to a host and tap \u2b50 to save it.",
            FontSize = 13,
            TextColor = Colors.Gray,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8, 0, 8)
        };

        _savedDeviceListLayout = new StackLayout { Spacing = 8 };
        _savedDeviceListLayout.Add(_savedEmptyLabel);
        root.Add(_savedDeviceListLayout);

        // Separator
        root.Add(new BoxView
        {
            Color = Color.FromArgb("#E0E0E0"),
            HeightRequest = 1,
            HorizontalOptions = LayoutOptions.Fill,
            Margin = new Thickness(0, 4, 0, 4)
        });

        // ── Discovered (Nearby) section ──
        root.Add(new Label
        {
            Text = "Nearby Devices",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#333333"),
            Margin = new Thickness(0, 0, 0, 0)
        });

        _countLabel = new Label
        {
            Text = "Scanning for devices on your network...",
            FontSize = 13,
            TextColor = Colors.Gray,
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.Add(_countLabel);

        _emptyLabel = new Label
        {
            Text = "No devices found yet.\nMake sure RemoteLink Desktop is running on the same network.",
            FontSize = 14,
            TextColor = Colors.Gray,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 20, 0, 0)
        };

        _deviceListLayout = new StackLayout { Spacing = 10 };
        _deviceListLayout.Add(_emptyLabel);
        root.Add(_deviceListLayout);

        return new ScrollView { Content = root };
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

    // ── Saved device cards ─────────────────────────────────────────────

    private void RefreshSavedDeviceList()
    {
        _savedDeviceListLayout.Clear();

        var saved = _savedDevices.GetAll();
        if (saved.Count == 0)
        {
            _savedDeviceListLayout.Add(_savedEmptyLabel);
            return;
        }

        foreach (var device in saved)
            _savedDeviceListLayout.Add(BuildSavedDeviceCard(device));
    }

    private View BuildSavedDeviceCard(SavedDevice saved)
    {
        var isConnected = _client.IsConnected && _client.ConnectedHost?.DeviceId == saved.DeviceId;

        var card = new Border
        {
            BackgroundColor = isConnected ? Color.FromArgb("#F3E8FF") : Color.FromArgb("#FFFBF0"),
            Stroke = isConnected ? Color.FromArgb("#512BD4") : Color.FromArgb("#E0D6B8"),
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
                new ColumnDefinition(GridLength.Auto),   // star icon
                new ColumnDefinition(GridLength.Star),    // info
                new ColumnDefinition(GridLength.Auto),    // actions
            },
            ColumnSpacing = 10,
            VerticalOptions = LayoutOptions.Center
        };

        // Star icon
        var starIcon = new Label
        {
            Text = "\u2b50",
            FontSize = 24,
            VerticalOptions = LayoutOptions.Center
        };
        Grid.SetColumn(starIcon, 0);

        // Device info
        var infoStack = new StackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };

        var displayName = !string.IsNullOrWhiteSpace(saved.FriendlyName)
            ? saved.FriendlyName
            : saved.DeviceName;

        infoStack.Add(new Label
        {
            Text = displayName,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.Black
        });

        if (!string.IsNullOrWhiteSpace(saved.FriendlyName) && saved.FriendlyName != saved.DeviceName)
        {
            infoStack.Add(new Label
            {
                Text = saved.DeviceName,
                FontSize = 11,
                TextColor = Colors.Gray,
                FontAttributes = FontAttributes.Italic
            });
        }

        infoStack.Add(new Label
        {
            Text = $"{saved.IPAddress}:{saved.Port}",
            FontSize = 12,
            TextColor = Colors.Gray
        });

        if (saved.LastConnected.HasValue)
        {
            infoStack.Add(new Label
            {
                Text = $"Last connected: {FormatRelativeTime(saved.LastConnected.Value)}",
                FontSize = 10,
                TextColor = Color.FromArgb("#888888")
            });
        }

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

        // Actions column
        var actionsStack = new StackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center
        };

        var editButton = new Button
        {
            Text = "\u270f",
            FontSize = 14,
            BackgroundColor = Color.FromArgb("#E8E0F0"),
            TextColor = Color.FromArgb("#512BD4"),
            CornerRadius = 4,
            WidthRequest = 36,
            HeightRequest = 32,
            Padding = 0
        };
        editButton.Clicked += async (_, _) => await OnEditSavedDevice(saved);

        var deleteButton = new Button
        {
            Text = "\ud83d\uddd1",
            FontSize = 14,
            BackgroundColor = Color.FromArgb("#FFE8E8"),
            TextColor = Color.FromArgb("#C62828"),
            CornerRadius = 4,
            WidthRequest = 36,
            HeightRequest = 32,
            Padding = 0
        };
        deleteButton.Clicked += async (_, _) => await OnDeleteSavedDevice(saved);

        actionsStack.Add(editButton);
        actionsStack.Add(deleteButton);
        Grid.SetColumn(actionsStack, 2);

        grid.Add(starIcon);
        grid.Add(infoStack);
        grid.Add(actionsStack);
        card.Content = grid;

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OnSavedDeviceTappedAsync(saved);
        card.GestureRecognizers.Add(tap);

        return card;
    }

    private async Task OnSavedDeviceTappedAsync(SavedDevice saved)
    {
        if (_client.IsConnected && _client.ConnectedHost?.DeviceId == saved.DeviceId)
        {
            Shell.Current.CurrentItem = Shell.Current.Items[0];
            return;
        }

        var pin = await DisplayPromptAsync(
            title: $"Connect to {saved.FriendlyName ?? saved.DeviceName}",
            message: "Enter the 6-digit PIN shown on the desktop host:",
            accept: "Connect",
            cancel: "Cancel",
            placeholder: "123456",
            maxLength: 6,
            keyboard: Keyboard.Numeric);

        if (string.IsNullOrWhiteSpace(pin)) return;

        var deviceInfo = new DeviceInfo
        {
            DeviceId = saved.DeviceId,
            DeviceName = saved.DeviceName,
            IPAddress = saved.IPAddress,
            Port = saved.Port,
            Type = saved.Type
        };

        var success = await _client.ConnectToHostAsync(deviceInfo, pin);

        if (success)
        {
            await _savedDevices.TouchLastConnectedAsync(saved.DeviceId);
            Shell.Current.CurrentItem = Shell.Current.Items[0];
        }
        else
        {
            await DisplayAlertAsync("Connection Failed", "Could not connect. Check the PIN and try again.", "OK");
        }
    }

    private async Task OnEditSavedDevice(SavedDevice saved)
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
        RefreshSavedDeviceList();
    }

    private async Task OnDeleteSavedDevice(SavedDevice saved)
    {
        var confirm = await DisplayAlertAsync(
            "Remove Device",
            $"Remove \"{saved.FriendlyName ?? saved.DeviceName}\" from your address book?",
            "Remove", "Cancel");

        if (!confirm) return;

        await _savedDevices.RemoveAsync(saved.Id);
        RefreshSavedDeviceList();
    }

    // ── Discovered device list ──────────────────────────────────────────

    private void RefreshDeviceList()
    {
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
        var isSaved = _savedDevices.FindByDeviceId(device.DeviceId) != null;

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

        // Right side: save button + connect chevron
        var rightStack = new StackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
        };

        if (!isSaved)
        {
            var saveButton = new Button
            {
                Text = "\u2b50 Save",
                FontSize = 11,
                BackgroundColor = Color.FromArgb("#FFF3E0"),
                TextColor = Color.FromArgb("#E65100"),
                CornerRadius = 4,
                HeightRequest = 28,
                Padding = new Thickness(8, 0)
            };
            saveButton.Clicked += async (_, _) => await OnSaveDiscoveredDevice(device);
            rightStack.Add(saveButton);
        }
        else
        {
            rightStack.Add(new Label
            {
                Text = "\u2b50 Saved",
                FontSize = 11,
                TextColor = Color.FromArgb("#E65100")
            });
        }

        rightStack.Add(new Label
        {
            Text = isConnected ? "Connected" : "Connect >",
            FontSize = 13,
            TextColor = isConnected ? Color.FromArgb("#512BD4") : Color.FromArgb("#999999")
        });
        Grid.SetColumn(rightStack, 2);

        grid.Add(icon);
        grid.Add(infoStack);
        grid.Add(rightStack);
        card.Content = grid;

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OnDeviceTappedAsync(device);
        card.GestureRecognizers.Add(tap);

        return card;
    }

    private async Task OnSaveDiscoveredDevice(DeviceInfo device)
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

        var saved = new SavedDevice
        {
            FriendlyName = friendlyName.Trim(),
            DeviceName = device.DeviceName,
            DeviceId = device.DeviceId,
            IPAddress = device.IPAddress,
            Port = device.Port,
            Type = device.Type,
            DateAdded = DateTime.UtcNow
        };

        await _savedDevices.AddOrUpdateAsync(saved);
        RefreshSavedDeviceList();
        RefreshDeviceList(); // update "Saved" badge on discovered card
    }

    private async Task OnDeviceTappedAsync(DeviceInfo device)
    {
        if (_client.IsConnected && _client.ConnectedHost?.DeviceId == device.DeviceId)
        {
            Shell.Current.CurrentItem = Shell.Current.Items[0];
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
            // Auto-save device on successful connection if not already saved
            var existing = _savedDevices.FindByDeviceId(device.DeviceId);
            if (existing != null)
            {
                await _savedDevices.TouchLastConnectedAsync(device.DeviceId);
            }
            else
            {
                var saved = new SavedDevice
                {
                    FriendlyName = device.DeviceName,
                    DeviceName = device.DeviceName,
                    DeviceId = device.DeviceId,
                    IPAddress = device.IPAddress,
                    Port = device.Port,
                    Type = device.Type,
                    LastConnected = DateTime.UtcNow,
                    DateAdded = DateTime.UtcNow
                };
                await _savedDevices.AddOrUpdateAsync(saved);
            }

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
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshSavedDeviceList();
            RefreshDeviceList();
        });
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static string FormatRelativeTime(DateTime utcTime)
    {
        var elapsed = DateTime.UtcNow - utcTime;

        if (elapsed.TotalMinutes < 1) return "just now";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays < 7) return $"{(int)elapsed.TotalDays}d ago";
        return utcTime.ToLocalTime().ToString("MMM d, yyyy");
    }
}
