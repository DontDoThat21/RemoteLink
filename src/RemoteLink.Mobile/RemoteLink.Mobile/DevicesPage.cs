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
    private readonly IConnectionHistoryService _connectionHistory;
    private readonly List<DeviceInfo> _devices = new();
    private Border _connectionBanner = null!;
    private Label _connectionBannerLabel = null!;

    // UI references — address book
    private StackLayout _savedDeviceListLayout = null!;
    private Label _savedEmptyLabel = null!;

    // UI references — discovered
    private StackLayout _deviceListLayout = null!;
    private Label _emptyLabel = null!;
    private Label _countLabel = null!;

    private bool _loaded;

    // Track connection start time for duration calculation
    private DateTime? _connectionStartedAt;

    public DevicesPage(ILogger<DevicesPage> logger, RemoteDesktopClient client, ISavedDevicesService savedDevices, IConnectionHistoryService connectionHistory)
    {
        _logger = logger;
        _client = client;
        _savedDevices = savedDevices;
        _connectionHistory = connectionHistory;

        Title = "Devices";
        RefreshTheme();

        _client.DeviceDiscovered += OnDeviceDiscovered;
        _client.DeviceLost += OnDeviceLost;
        _client.ConnectionStateChanged += OnConnectionStateChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        ThemeColors.ThemeChanged += OnThemeChanged;

        RefreshTheme();

        if (!_loaded)
        {
            await _savedDevices.LoadAsync();
            _loaded = true;
        }

        RefreshSavedDeviceList();
        RefreshDeviceList();
        UpdateConnectionBanner();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ThemeColors.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshTheme();
            RefreshSavedDeviceList();
            RefreshDeviceList();
            UpdateConnectionBanner();
        });
    }

    private void RefreshTheme()
    {
        BackgroundColor = ThemeColors.PageBackground;
        Content = BuildLayout();
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
            TextColor = ThemeColors.Accent,
            Margin = new Thickness(0, 8, 0, 0)
        });

        // Connection status banner
        root.Add(BuildConnectionBanner());

        // ── Recent Connections button ──
        var historyButton = new Button
        {
            Text = "\ud83d\udd52  Recent Connections",
            FontSize = 14,
            BackgroundColor = ThemeColors.AccentSoft,
            TextColor = ThemeColors.Accent,
            CornerRadius = 8,
            HeightRequest = 42,
            HorizontalOptions = LayoutOptions.Fill
        };
        historyButton.Clicked += async (_, _) =>
        {
            var page = Handler?.MauiContext?.Services.GetService<RecentConnectionsPage>();
            if (page != null)
                await Navigation.PushAsync(page);
        };
        root.Add(historyButton);

        // ── Address Book section ──
        root.Add(new Label
        {
            Text = "Address Book",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.TextPrimary,
            Margin = new Thickness(0, 4, 0, 0)
        });

        root.Add(new Label
        {
            Text = "Saved devices for quick reconnection",
            FontSize = 12,
            TextColor = ThemeColors.TextSecondary,
            Margin = new Thickness(0, 0, 0, 4)
        });

        _savedEmptyLabel = new Label
        {
            Text = "No saved devices yet.\nConnect to a host and tap \u2b50 to save it.",
            FontSize = 13,
            TextColor = ThemeColors.TextSecondary,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8, 0, 8)
        };

        _savedDeviceListLayout = new StackLayout { Spacing = 8 };
        _savedDeviceListLayout.Add(_savedEmptyLabel);
        root.Add(_savedDeviceListLayout);

        // Separator
        root.Add(new BoxView
        {
            Color = ThemeColors.Divider,
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
            TextColor = ThemeColors.TextPrimary,
            Margin = new Thickness(0, 0, 0, 0)
        });

        _countLabel = new Label
        {
            Text = "Scanning for devices on your network...",
            FontSize = 13,
            TextColor = ThemeColors.TextSecondary,
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.Add(_countLabel);

        _emptyLabel = new Label
        {
            Text = "No devices found yet.\nMake sure RemoteLink Desktop is running on the same network.",
            FontSize = 14,
            TextColor = ThemeColors.TextSecondary,
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
        _connectionBanner = new Border
        {
            BackgroundColor = ThemeColors.SuccessBackground,
            Stroke = ThemeColors.SuccessBorder,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(12),
            IsVisible = false,
            AutomationId = "connection-banner"
        };

        _connectionBannerLabel = new Label
        {
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.SuccessText,
            AutomationId = "connection-label"
        };

        _connectionBanner.Content = _connectionBannerLabel;
        return _connectionBanner;
    }

    private void UpdateConnectionBanner()
    {
        if (_connectionBanner == null || _connectionBannerLabel == null)
            return;

        _connectionBanner.IsVisible = _client.IsConnected;
        if (!_client.IsConnected)
        {
            _connectionBannerLabel.Text = string.Empty;
            return;
        }

        var connectedHost = _client.ConnectedHost;
        var internetId = DeviceIdentityManager.FormatInternetDeviceId(connectedHost?.InternetDeviceId ?? connectedHost?.DeviceId);
        _connectionBannerLabel.Text = string.IsNullOrWhiteSpace(internetId)
            ? $"Connected to {connectedHost?.DeviceName ?? "Unknown"}"
            : $"Connected to {connectedHost?.DeviceName ?? "Unknown"} ({internetId})";
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
        var isConnected = _client.IsConnected && DeviceIdentityManager.MatchesDevice(saved, _client.ConnectedHost);

        var card = new Border
        {
            BackgroundColor = isConnected ? ThemeColors.SelectedCardBackground : ThemeColors.AddressBookBackground,
            Stroke = isConnected ? ThemeColors.SelectedCardBorder : ThemeColors.AddressBookBorder,
            StrokeThickness = isConnected ? 2 : 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14),
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(ThemeColors.ShadowColor),
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
            TextColor = ThemeColors.TextPrimary
        });

        if (!string.IsNullOrWhiteSpace(saved.FriendlyName) && saved.FriendlyName != saved.DeviceName)
        {
            infoStack.Add(new Label
            {
                Text = saved.DeviceName,
                FontSize = 11,
                TextColor = ThemeColors.TextSecondary,
                FontAttributes = FontAttributes.Italic
            });
        }

        infoStack.Add(new Label
        {
            Text = $"{saved.IPAddress}:{saved.Port}",
            FontSize = 12,
            TextColor = ThemeColors.TextSecondary
        });

        var formattedInternetId = DeviceIdentityManager.FormatInternetDeviceId(saved.InternetDeviceId);
        if (!string.IsNullOrWhiteSpace(formattedInternetId))
        {
            infoStack.Add(new Label
            {
                Text = $"ID: {formattedInternetId}",
                FontSize = 11,
                TextColor = ThemeColors.Accent
            });
        }

        if (saved.LastConnected.HasValue)
        {
            infoStack.Add(new Label
            {
                Text = $"Last connected: {FormatRelativeTime(saved.LastConnected.Value)}",
                FontSize = 10,
                TextColor = ThemeColors.TextMuted
            });
        }

        if (isConnected)
        {
            infoStack.Add(new Label
            {
                Text = "Currently connected",
                FontSize = 11,
                TextColor = ThemeColors.Accent,
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
            BackgroundColor = ThemeColors.SecondaryButtonBackground,
            TextColor = ThemeColors.SecondaryButtonText,
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
            BackgroundColor = ThemeColors.DangerSoft,
            TextColor = ThemeColors.Danger,
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
        if (_client.IsConnected && DeviceIdentityManager.MatchesDevice(saved, _client.ConnectedHost))
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
            InternetDeviceId = saved.InternetDeviceId,
            DeviceName = saved.DeviceName,
            IPAddress = saved.IPAddress,
            Port = saved.Port,
            SupportsRelay = saved.SupportsRelay,
            RelayServerHost = saved.RelayServerHost,
            RelayServerPort = saved.RelayServerPort,
            Type = saved.Type
        };

        var success = await _client.ConnectToHostAsync(deviceInfo, pin);

        if (success)
        {
            _connectionStartedAt = DateTime.UtcNow;
            await _savedDevices.TouchLastConnectedAsync(saved.DeviceId);
            await RecordConnectionAsync(deviceInfo, ConnectionOutcome.Success);
            Shell.Current.CurrentItem = Shell.Current.Items[0];
        }
        else
        {
            await RecordConnectionAsync(deviceInfo, ConnectionOutcome.Failed);
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
        var isConnected = _client.IsConnected && DeviceIdentityManager.MatchesDevice(device, _client.ConnectedHost);
        var isSaved = _savedDevices.FindMatchingDevice(device) != null;

        var card = new Border
        {
            BackgroundColor = isConnected ? ThemeColors.SelectedCardBackground : ThemeColors.CardBackground,
            Stroke = isConnected ? ThemeColors.SelectedCardBorder : ThemeColors.CardBorder,
            StrokeThickness = isConnected ? 2 : 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14),
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(ThemeColors.ShadowColor),
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
            TextColor = ThemeColors.TextPrimary
        });
        infoStack.Add(new Label
        {
            Text = $"{device.IPAddress}:{device.Port}",
            FontSize = 12,
            TextColor = ThemeColors.TextSecondary
        });

        var formattedInternetId = DeviceIdentityManager.FormatInternetDeviceId(device.InternetDeviceId);
        if (!string.IsNullOrWhiteSpace(formattedInternetId))
        {
            infoStack.Add(new Label
            {
                Text = $"ID: {formattedInternetId}",
                FontSize = 11,
                TextColor = ThemeColors.Accent
            });
        }

        if (isConnected)
        {
            infoStack.Add(new Label
            {
                Text = "Currently connected",
                FontSize = 11,
                TextColor = ThemeColors.Accent,
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
                BackgroundColor = ThemeColors.WarningSoft,
                TextColor = ThemeColors.WarningText,
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
                TextColor = ThemeColors.WarningText
            });
        }

        rightStack.Add(new Label
        {
            Text = isConnected ? "Connected" : "Connect >",
            FontSize = 13,
            TextColor = isConnected ? ThemeColors.Accent : ThemeColors.TextMuted
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
            InternetDeviceId = DeviceIdentityManager.NormalizeInternetDeviceId(device.InternetDeviceId),
            IPAddress = device.IPAddress,
            Port = device.Port,
            SupportsRelay = device.SupportsRelay,
            RelayServerHost = device.RelayServerHost,
            RelayServerPort = device.RelayServerPort,
            Type = device.Type,
            DateAdded = DateTime.UtcNow
        };

        await _savedDevices.AddOrUpdateAsync(saved);
        RefreshSavedDeviceList();
        RefreshDeviceList(); // update "Saved" badge on discovered card
    }

    private async Task OnDeviceTappedAsync(DeviceInfo device)
    {
        if (_client.IsConnected && DeviceIdentityManager.MatchesDevice(device, _client.ConnectedHost))
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
            _connectionStartedAt = DateTime.UtcNow;

            // Auto-save device on successful connection if not already saved
            var existing = _savedDevices.FindMatchingDevice(device);
            if (existing != null)
            {
                existing.DeviceId = device.DeviceId;
                existing.DeviceName = device.DeviceName;
                existing.InternetDeviceId = DeviceIdentityManager.NormalizeInternetDeviceId(device.InternetDeviceId);
                existing.IPAddress = device.IPAddress;
                existing.Port = device.Port;
                existing.SupportsRelay = device.SupportsRelay;
                existing.RelayServerHost = device.RelayServerHost;
                existing.RelayServerPort = device.RelayServerPort;
                existing.Type = device.Type;
                existing.LastConnected = DateTime.UtcNow;
                await _savedDevices.AddOrUpdateAsync(existing);
            }
            else
            {
                var saved = new SavedDevice
                {
                    FriendlyName = device.DeviceName,
                    DeviceName = device.DeviceName,
                    DeviceId = device.DeviceId,
                    InternetDeviceId = DeviceIdentityManager.NormalizeInternetDeviceId(device.InternetDeviceId),
                    IPAddress = device.IPAddress,
                    Port = device.Port,
                    SupportsRelay = device.SupportsRelay,
                    RelayServerHost = device.RelayServerHost,
                    RelayServerPort = device.RelayServerPort,
                    Type = device.Type,
                    LastConnected = DateTime.UtcNow,
                    DateAdded = DateTime.UtcNow
                };
                await _savedDevices.AddOrUpdateAsync(saved);
            }

            await RecordConnectionAsync(device, ConnectionOutcome.Success);
            Shell.Current.CurrentItem = Shell.Current.Items[0];
        }
        else
        {
            await RecordConnectionAsync(device, ConnectionOutcome.Failed);
            await DisplayAlertAsync("Connection Failed", "Could not connect. Check the PIN and try again.", "OK");
        }
    }

    // ── Event handlers ─────────────────────────────────────────────────

    private void OnDeviceDiscovered(object? sender, DeviceInfo device)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var existing = _devices.FirstOrDefault(d => d.DeviceId == device.DeviceId);
            if (existing == null)
            {
                _devices.Add(device);
            }
            else
            {
                var index = _devices.IndexOf(existing);
                _devices[index] = device;
            }

            RefreshDeviceList();
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
            if (state == ClientConnectionState.Disconnected && _connectionStartedAt.HasValue)
            {
                // Update the last connection record with disconnect time and duration
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

            UpdateConnectionBanner();
            RefreshSavedDeviceList();
            RefreshDeviceList();
        });
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private async Task RecordConnectionAsync(DeviceInfo device, ConnectionOutcome outcome)
    {
        var record = new ConnectionRecord
        {
            DeviceName = device.DeviceName,
            DeviceId = device.DeviceId,
            InternetDeviceId = DeviceIdentityManager.NormalizeInternetDeviceId(device.InternetDeviceId),
            IPAddress = device.IPAddress,
            Port = device.Port,
            ConnectedAt = DateTime.UtcNow,
            Outcome = outcome
        };
        await _connectionHistory.AddAsync(record);
    }

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
