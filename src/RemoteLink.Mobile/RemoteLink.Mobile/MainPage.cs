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

        // Command execution section
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

        // Enable button when command is entered
        commandEntry.TextChanged += (s, e) =>
        {
            executeButton.IsEnabled = !string.IsNullOrWhiteSpace(e.NewTextValue);
        };

        // Handle command execution
        executeButton.Clicked += async (s, e) =>
        {
            await ExecuteCommandAsync(commandEntry.Text, workingDirectoryEntry.Text, resultLabel);
        };

        mainLayout.Children.Add(titleLabel);
        mainLayout.Children.Add(statusLabel);
        mainLayout.Children.Add(activityIndicator);
        mainLayout.Children.Add(hostListLabel);
        mainLayout.Children.Add(commandSectionLabel);
        mainLayout.Children.Add(commandEntry);
        mainLayout.Children.Add(workingDirectoryEntry);
        mainLayout.Children.Add(executeButton);
        mainLayout.Children.Add(resultLabel);

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

            // Create input event for command execution
            var inputEvent = new RemoteLink.Shared.Models.InputEvent
            {
                Type = RemoteLink.Shared.Models.InputEventType.CommandExecution,
                Command = command,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory
            };

            // For now, since we don't have full communication service implementation,
            // we'll simulate the command execution locally for demonstration
            await SimulateCommandExecutionAsync(inputEvent, resultLabel);
        }
        catch (Exception ex)
        {
            resultLabel.Text = $"Error: {ex.Message}";
            resultLabel.TextColor = Colors.Red;
        }
    }

    private async Task SimulateCommandExecutionAsync(RemoteLink.Shared.Models.InputEvent inputEvent, Label resultLabel)
    {
        // Simulate sending to desktop host and getting response
        await Task.Delay(1000); // Simulate network delay

        // For demonstration, show what would be sent
        var commandInfo = $"Command: {inputEvent.Command}\n";
        if (!string.IsNullOrEmpty(inputEvent.WorkingDirectory))
        {
            commandInfo += $"Working Directory: {inputEvent.WorkingDirectory}\n";
        }
        commandInfo += $"Event ID: {inputEvent.EventId}\n";
        commandInfo += "Status: Command sent to desktop host";

        resultLabel.Text = commandInfo;
        resultLabel.TextColor = Colors.Green;
    }
}