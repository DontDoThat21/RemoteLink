using System.ComponentModel;
using RemoteLink.Mobile.Services;

namespace RemoteLink.Mobile;

public partial class MainPage : ContentPage, INotifyPropertyChanged
{
    private bool _isDiscovering;
    private string _statusMessage = "Initializing...";
    private readonly List<RemoteLink.Shared.Models.DeviceInfo> _availableHosts = new();

    public bool IsDiscovering
    {
        get => _isDiscovering;
        set
        {
            _isDiscovering = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public List<RemoteLink.Shared.Models.DeviceInfo> AvailableHosts => _availableHosts;

    public MainPage()
    {
        Title = "RemoteLink Mobile";
        BackgroundColor = Colors.White;
        
        // Create the main layout
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
        activityIndicator.SetBinding(ActivityIndicator.IsRunningProperty, new Binding(nameof(IsDiscovering), source: this));

        // Host list placeholder
        var hostListLabel = new Label
        {
            Text = "Discovered hosts will appear here",
            FontSize = 14,
            HorizontalOptions = LayoutOptions.Center,
            TextColor = Colors.Gray
        };

        mainLayout.Children.Add(titleLabel);
        mainLayout.Children.Add(statusLabel);
        mainLayout.Children.Add(activityIndicator);
        mainLayout.Children.Add(hostListLabel);

        Content = new ScrollView { Content = mainLayout };

        // Start discovery
        _ = StartDiscoveryAsync();
    }

    private async Task StartDiscoveryAsync()
    {
        try
        {
            StatusMessage = "Starting discovery service...";
            IsDiscovering = true;
            
            // Create and configure the service directly since DI might not be available yet
            var localDevice = new RemoteLink.Shared.Models.DeviceInfo
            {
                DeviceId = Environment.MachineName + "_Mobile_" + Guid.NewGuid().ToString("N")[..8],
                DeviceName = Environment.MachineName + " Mobile",
                Type = RemoteLink.Shared.Models.DeviceType.Mobile,
                Port = 12347
            };
            var networkDiscovery = new RemoteLink.Shared.Services.UdpNetworkDiscovery(localDevice);
            var remoteDesktopClient = new RemoteDesktopClient(null!, networkDiscovery);
            
            // Subscribe to events
            remoteDesktopClient.DeviceDiscovered += OnDeviceDiscovered;
            remoteDesktopClient.DeviceLost += OnDeviceLost;
            remoteDesktopClient.ServiceStatusChanged += OnServiceStatusChanged;
            
            await remoteDesktopClient.StartAsync();
            
            StatusMessage = "Searching for desktop hosts...";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error starting discovery: {ex.Message}";
            IsDiscovering = false;
        }
    }

    private void OnDeviceDiscovered(object? sender, RemoteLink.Shared.Models.DeviceInfo device)
    {
        if (device.Type == RemoteLink.Shared.Models.DeviceType.Desktop)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!_availableHosts.Any(h => h.DeviceId == device.DeviceId))
                {
                    _availableHosts.Add(device);
                    StatusMessage = $"Found {_availableHosts.Count} desktop host(s)";
                }
            });
        }
    }

    private void OnDeviceLost(object? sender, RemoteLink.Shared.Models.DeviceInfo device)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var existingHost = _availableHosts.FirstOrDefault(h => h.DeviceId == device.DeviceId);
            if (existingHost != null)
            {
                _availableHosts.Remove(existingHost);
                StatusMessage = $"Found {_availableHosts.Count} desktop host(s)";
            }
        });
    }

    private void OnServiceStatusChanged(object? sender, string status)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Could update a status indicator here
        });
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected override void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}