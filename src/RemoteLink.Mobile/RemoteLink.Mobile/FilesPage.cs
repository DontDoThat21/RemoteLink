using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;

namespace RemoteLink.Mobile;

/// <summary>
/// Files tab: send, receive, and track file transfers with the connected desktop host.
/// </summary>
public class FilesPage : ContentPage
{
    private readonly ILogger<FilesPage> _logger;
    private readonly ILogger<FileTransferService> _fileTransferLogger;
    private readonly RemoteDesktopClient _client;
    private readonly Dictionary<string, FileTransferRequest> _pendingRequests = new();
    private readonly Dictionary<string, TransferItem> _transferItems = new();

    private ICommunicationService? _boundCommunicationService;
    private IFileTransferService? _fileTransferService;

    private Label _connectionLabel = null!;
    private Label _savePathLabel = null!;
    private Button _sendButton = null!;
    private StackLayout _incomingRequestsLayout = null!;
    private Label _incomingEmptyLabel = null!;
    private StackLayout _transfersLayout = null!;
    private Label _transfersEmptyLabel = null!;

    public FilesPage(ILogger<FilesPage> logger, ILogger<FileTransferService> fileTransferLogger, RemoteDesktopClient client)
    {
        _logger = logger;
        _fileTransferLogger = fileTransferLogger;
        _client = client;

        Title = "Files";
        RefreshTheme();

        _client.ConnectionStateChanged += OnConnectionStateChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ThemeColors.ThemeChanged += OnThemeChanged;
        RefreshTheme();
        EnsureFileTransferService();
        UpdateConnectionUi();
        RefreshIncomingRequests();
        RefreshTransfers();
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
            UpdateConnectionUi();
            RefreshIncomingRequests();
            RefreshTransfers();
        });
    }

    private void RefreshTheme()
    {
        BackgroundColor = ThemeColors.PageBackground;
        Content = new ScrollView { Content = BuildLayout() };
    }

    private View BuildLayout()
    {
        var root = new StackLayout
        {
            Padding = new Thickness(16),
            Spacing = 12
        };

        root.Add(new Label
        {
            Text = "Files",
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.Accent,
            Margin = new Thickness(0, 8, 0, 0)
        });

        root.Add(new Label
        {
            Text = "Send files to your connected desktop host or accept incoming transfers.",
            FontSize = 13,
            TextColor = ThemeColors.TextSecondary
        });

        root.Add(BuildConnectionCard());
        root.Add(BuildSendCard());
        root.Add(BuildIncomingRequestsSection());
        root.Add(BuildTransfersSection());

        return root;
    }

    private View BuildConnectionCard()
    {
        var border = new Border
        {
            BackgroundColor = ThemeColors.SurfaceBackground,
            Stroke = ThemeColors.ToolbarBorder,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(14)
        };

        _connectionLabel = new Label
        {
            FontSize = 14,
            TextColor = ThemeColors.Accent
        };

        border.Content = new StackLayout
        {
            Spacing = 6,
            Children =
            {
                new Label
                {
                    Text = "Connection",
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = ThemeColors.TextPrimary
                },
                _connectionLabel
            }
        };

        return border;
    }

    private View BuildSendCard()
    {
        var border = new Border
        {
            BackgroundColor = ThemeColors.CardBackground,
            Stroke = ThemeColors.CardBorder,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(14)
        };

        _savePathLabel = new Label
        {
            FontSize = 12,
            TextColor = ThemeColors.TextSecondary,
            Text = $"Incoming files are saved to: {GetReceiveDirectory()}"
        };

        _sendButton = new Button
        {
            Text = "Browse and Send File",
            BackgroundColor = ThemeColors.Accent,
            TextColor = Colors.White,
            CornerRadius = 10,
            HeightRequest = 46
        };
        _sendButton.Clicked += OnSendFileClicked;

        border.Content = new StackLayout
        {
            Spacing = 10,
            Children =
            {
                new Label
                {
                    Text = "Send to Desktop",
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = ThemeColors.TextPrimary
                },
                new Label
                {
                    Text = "Choose a local file and upload it to the connected RemoteLink desktop session.",
                    FontSize = 13,
                    TextColor = ThemeColors.TextSecondary
                },
                _sendButton,
                _savePathLabel
            }
        };

        return border;
    }

    private View BuildIncomingRequestsSection()
    {
        _incomingRequestsLayout = new StackLayout { Spacing = 10 };
        _incomingEmptyLabel = new Label
        {
            Text = "No incoming file requests.",
            FontSize = 13,
            TextColor = ThemeColors.TextSecondary,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };

        return new Border
        {
            BackgroundColor = ThemeColors.CardBackground,
            Stroke = ThemeColors.CardBorder,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(14),
            Content = new StackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Label
                    {
                        Text = "Incoming Requests",
                        FontSize = 16,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = ThemeColors.TextPrimary
                    },
                    new Label
                    {
                        Text = "Approve or decline files sent from the connected desktop host.",
                        FontSize = 13,
                        TextColor = ThemeColors.TextSecondary
                    },
                    _incomingRequestsLayout
                }
            }
        };
    }

    private View BuildTransfersSection()
    {
        _transfersLayout = new StackLayout { Spacing = 10 };
        _transfersEmptyLabel = new Label
        {
            Text = "No transfers yet.",
            FontSize = 13,
            TextColor = ThemeColors.TextSecondary,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };

        return new Border
        {
            BackgroundColor = ThemeColors.CardBackground,
            Stroke = ThemeColors.CardBorder,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(14),
            Content = new StackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Label
                    {
                        Text = "Transfer Activity",
                        FontSize = 16,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = ThemeColors.TextPrimary
                    },
                    new Label
                    {
                        Text = "Track upload and download progress for this mobile session.",
                        FontSize = 13,
                        TextColor = ThemeColors.TextSecondary
                    },
                    _transfersLayout
                }
            }
        };
    }

    private void EnsureFileTransferService()
    {
        var communicationService = _client.CurrentCommunicationService;
        if (!_client.IsConnected || communicationService is null)
        {
            if (_client.ConnectionState == ClientConnectionState.Disconnected)
            {
                DetachFileTransferEvents();
                _boundCommunicationService = null;
                _fileTransferService = null;
            }

            return;
        }

        if (ReferenceEquals(_boundCommunicationService, communicationService) && _fileTransferService is not null)
            return;

        DetachFileTransferEvents();

        _boundCommunicationService = communicationService;
        _fileTransferService = new FileTransferService(_fileTransferLogger, communicationService);
        _fileTransferService.TransferRequested += OnTransferRequested;
        _fileTransferService.TransferResponseReceived += OnTransferResponseReceived;
        _fileTransferService.ProgressUpdated += OnProgressUpdated;
        _fileTransferService.TransferCompleted += OnTransferCompleted;
    }

    private void DetachFileTransferEvents()
    {
        if (_fileTransferService is null)
            return;

        _fileTransferService.TransferRequested -= OnTransferRequested;
        _fileTransferService.TransferResponseReceived -= OnTransferResponseReceived;
        _fileTransferService.ProgressUpdated -= OnProgressUpdated;
        _fileTransferService.TransferCompleted -= OnTransferCompleted;
    }

    private void UpdateConnectionUi()
    {
        var isConnected = _client.IsConnected;
        var fileTransferAllowed = _client.CurrentSessionPermissions?.AllowFileTransfer != false;
        var hostName = _client.ConnectedHost?.DeviceName ?? "desktop host";

        _connectionLabel.Text = !isConnected
            ? "Connect to a desktop host first to send or receive files."
            : fileTransferAllowed
                ? $"Connected to {hostName}. File transfer is ready."
                : $"Connected to {hostName}. File transfer is disabled for this session.";

        _sendButton.IsEnabled = isConnected && fileTransferAllowed;
        _sendButton.BackgroundColor = _sendButton.IsEnabled ? ThemeColors.Accent : ThemeColors.NeutralButtonBackground;
        _savePathLabel.Text = $"Incoming files are saved to: {GetReceiveDirectory()}";

        if (!isConnected)
        {
            foreach (var item in _transferItems.Values.Where(item => !item.IsCompleted))
            {
                item.IsCompleted = true;
                item.IsSuccessful = false;
                item.Status = "Connection lost";
                item.UpdatedAt = DateTime.UtcNow;
            }

            _pendingRequests.Clear();
        }
    }

    private async void OnSendFileClicked(object? sender, EventArgs e)
    {
        if (!_client.IsConnected)
        {
            await DisplayAlertAsync("No Connection", "Connect to a desktop host before sending files.", "OK");
            return;
        }

        if (_client.CurrentSessionPermissions?.AllowFileTransfer == false)
        {
            await DisplayAlertAsync("Unavailable", "The host has disabled file transfer for this session.", "OK");
            return;
        }

        EnsureFileTransferService();
        if (_fileTransferService is null)
        {
            await DisplayAlertAsync("Unavailable", "File transfer is not available for the current connection.", "OK");
            return;
        }

        try
        {
            var result = await FilePicker.Default.PickAsync();
            if (result is null)
                return;

            var localPath = await PreparePickedFileAsync(result);
            var fileInfo = new FileInfo(localPath);
            var transferId = await _fileTransferService.InitiateTransferAsync(localPath, FileTransferDirection.Upload);

            _transferItems[transferId] = new TransferItem
            {
                TransferId = transferId,
                FileName = string.IsNullOrWhiteSpace(result.FileName) ? Path.GetFileName(localPath) : result.FileName,
                DirectionLabel = "Outgoing",
                Status = "Waiting for desktop approval...",
                TotalBytes = fileInfo.Exists ? fileInfo.Length : 0,
                LocalPath = localPath,
                UpdatedAt = DateTime.UtcNow
            };

            RefreshTransfers();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate mobile file transfer");
            await DisplayAlertAsync("File Transfer Failed", ex.Message, "OK");
        }
    }

    private async Task<string> PreparePickedFileAsync(FileResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.FullPath) && File.Exists(result.FullPath))
            return result.FullPath;

        var fileName = string.IsNullOrWhiteSpace(result.FileName) ? $"upload_{Guid.NewGuid():N}" : SanitizeFileName(result.FileName);
        var uploadsDirectory = Path.Combine(FileSystem.Current.CacheDirectory, "uploads");
        Directory.CreateDirectory(uploadsDirectory);

        var localPath = GetUniquePath(uploadsDirectory, fileName);

        await using var source = await result.OpenReadAsync();
        await using var destination = File.Create(localPath);
        await source.CopyToAsync(destination);

        return localPath;
    }

    private void OnTransferRequested(object? sender, FileTransferRequest request)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _pendingRequests[request.TransferId] = request;
            _transferItems[request.TransferId] = new TransferItem
            {
                TransferId = request.TransferId,
                FileName = request.FileName,
                DirectionLabel = "Incoming",
                Status = "Awaiting your approval",
                TotalBytes = request.FileSize,
                UpdatedAt = DateTime.UtcNow
            };

            RefreshIncomingRequests();
            RefreshTransfers();
        });
    }

    private void OnTransferResponseReceived(object? sender, FileTransferResponse response)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_transferItems.TryGetValue(response.TransferId, out var item))
                return;

            item.Status = response.Accepted
                ? "Transferring..."
                : $"Declined: {response.Message ?? response.RejectionReason?.ToString() ?? "Rejected"}";
            item.IsCompleted = !response.Accepted;
            item.IsSuccessful = response.Accepted;
            item.UpdatedAt = DateTime.UtcNow;

            RefreshTransfers();
        });
    }

    private void OnProgressUpdated(object? sender, FileTransferProgress progress)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_transferItems.TryGetValue(progress.TransferId, out var item))
            {
                item = new TransferItem
                {
                    TransferId = progress.TransferId,
                    FileName = $"Transfer {progress.TransferId[..Math.Min(8, progress.TransferId.Length)]}",
                    DirectionLabel = "Transfer"
                };
                _transferItems[progress.TransferId] = item;
            }

            item.BytesTransferred = progress.BytesTransferred;
            item.TotalBytes = progress.TotalBytes > 0 ? progress.TotalBytes : item.TotalBytes;
            item.Status = item.IsCompleted ? item.Status : "Transferring...";
            item.BytesPerSecond = progress.BytesPerSecond;
            item.UpdatedAt = DateTime.UtcNow;

            RefreshTransfers();
        });
    }

    private void OnTransferCompleted(object? sender, FileTransferComplete complete)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _pendingRequests.Remove(complete.TransferId);

            if (!_transferItems.TryGetValue(complete.TransferId, out var item))
            {
                item = new TransferItem
                {
                    TransferId = complete.TransferId,
                    FileName = $"Transfer {complete.TransferId[..Math.Min(8, complete.TransferId.Length)]}",
                    DirectionLabel = "Transfer"
                };
                _transferItems[complete.TransferId] = item;
            }

            item.IsCompleted = true;
            item.IsSuccessful = complete.Success;
            item.Status = complete.Success ? "Completed" : (complete.ErrorMessage ?? "Transfer failed");
            item.SavedPath = complete.SavedPath ?? item.SavedPath;
            if (item.TotalBytes > 0 && complete.Success)
                item.BytesTransferred = item.TotalBytes;

            item.UpdatedAt = DateTime.UtcNow;

            RefreshIncomingRequests();
            RefreshTransfers();
        });
    }

    private async Task AcceptRequestAsync(string transferId)
    {
        EnsureFileTransferService();
        if (_fileTransferService is null || !_pendingRequests.TryGetValue(transferId, out var request))
            return;

        try
        {
            var savePath = GetUniquePath(GetReceiveDirectory(), SanitizeFileName(request.FileName));
            await _fileTransferService.AcceptTransferAsync(transferId, savePath);

            _pendingRequests.Remove(transferId);
            if (_transferItems.TryGetValue(transferId, out var item))
            {
                item.DirectionLabel = "Incoming";
                item.Status = "Receiving...";
                item.TotalBytes = request.FileSize;
                item.SavedPath = savePath;
                item.UpdatedAt = DateTime.UtcNow;
            }

            RefreshIncomingRequests();
            RefreshTransfers();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to accept transfer {TransferId}", transferId);
            await DisplayAlertAsync("Accept Transfer Failed", ex.Message, "OK");
        }
    }

    private async Task RejectRequestAsync(string transferId)
    {
        EnsureFileTransferService();
        if (_fileTransferService is null || !_pendingRequests.ContainsKey(transferId))
            return;

        try
        {
            await _fileTransferService.RejectTransferAsync(transferId, FileTransferRejectionReason.UserDeclined);
            _pendingRequests.Remove(transferId);

            if (_transferItems.TryGetValue(transferId, out var item))
            {
                item.IsCompleted = true;
                item.IsSuccessful = false;
                item.Status = "Declined on mobile";
                item.UpdatedAt = DateTime.UtcNow;
            }

            RefreshIncomingRequests();
            RefreshTransfers();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reject transfer {TransferId}", transferId);
            await DisplayAlertAsync("Reject Transfer Failed", ex.Message, "OK");
        }
    }

    private async Task CancelTransferAsync(string transferId)
    {
        EnsureFileTransferService();
        if (_fileTransferService is null)
            return;

        try
        {
            if (await _fileTransferService.CancelTransferAsync(transferId) && _transferItems.TryGetValue(transferId, out var item))
            {
                item.IsCompleted = true;
                item.IsSuccessful = false;
                item.Status = "Cancelled";
                item.UpdatedAt = DateTime.UtcNow;
                RefreshTransfers();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel transfer {TransferId}", transferId);
            await DisplayAlertAsync("Cancel Transfer Failed", ex.Message, "OK");
        }
    }

    private async Task OpenTransferAsync(string path)
    {
        if (!File.Exists(path))
        {
            await DisplayAlertAsync("File Not Found", "The transferred file could not be found on disk.", "OK");
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(new OpenFileRequest(Path.GetFileName(path), new ReadOnlyFile(path)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open transferred file {Path}", path);
            await DisplayAlertAsync("Open Failed", ex.Message, "OK");
        }
    }

    private async Task ShareTransferAsync(string path)
    {
        if (!File.Exists(path))
        {
            await DisplayAlertAsync("File Not Found", "The transferred file could not be found on disk.", "OK");
            return;
        }

        try
        {
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Share transferred file",
                File = new ShareFile(path)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to share transferred file {Path}", path);
            await DisplayAlertAsync("Share Failed", ex.Message, "OK");
        }
    }

    private void RefreshIncomingRequests()
    {
        _incomingRequestsLayout.Children.Clear();

        if (_pendingRequests.Count == 0)
        {
            _incomingRequestsLayout.Children.Add(_incomingEmptyLabel);
            return;
        }

        foreach (var request in _pendingRequests.Values.OrderByDescending(request => request.Timestamp))
        {
            var acceptButton = new Button
            {
                Text = "Accept",
                BackgroundColor = ThemeColors.Success,
                TextColor = Colors.White,
                CornerRadius = 8,
                HorizontalOptions = LayoutOptions.Fill,
                WidthRequest = 110
            };
            acceptButton.Clicked += async (_, _) => await AcceptRequestAsync(request.TransferId);

            var rejectButton = new Button
            {
                Text = "Decline",
                BackgroundColor = ThemeColors.Danger,
                TextColor = Colors.White,
                CornerRadius = 8,
                HorizontalOptions = LayoutOptions.Fill,
                WidthRequest = 110
            };
            rejectButton.Clicked += async (_, _) => await RejectRequestAsync(request.TransferId);

            _incomingRequestsLayout.Children.Add(new Border
            {
                BackgroundColor = ThemeColors.PlaceholderBackground,
                Stroke = ThemeColors.CardBorder,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                Padding = new Thickness(12),
                Content = new StackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        new Label
                        {
                            Text = request.FileName,
                            FontSize = 15,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = ThemeColors.TextPrimary
                        },
                        new Label
                        {
                            Text = $"{FormatBytes(request.FileSize)} • {request.MimeType}",
                            FontSize = 12,
                            TextColor = ThemeColors.TextSecondary
                        },
                        new HorizontalStackLayout
                        {
                            Spacing = 8,
                            Children = { acceptButton, rejectButton }
                        }
                    }
                }
            });
        }
    }

    private void RefreshTransfers()
    {
        _transfersLayout.Children.Clear();

        if (_transferItems.Count == 0)
        {
            _transfersLayout.Children.Add(_transfersEmptyLabel);
            return;
        }

        foreach (var item in _transferItems.Values.OrderByDescending(item => item.UpdatedAt))
        {
            var progress = item.TotalBytes > 0
                ? Math.Clamp(item.BytesTransferred / (double)item.TotalBytes, 0, 1)
                : 0;

            var statusColor = item.IsCompleted
                ? (item.IsSuccessful ? ThemeColors.SuccessText : ThemeColors.DangerText)
                : ThemeColors.Accent;

            var detailsText = item.TotalBytes > 0
                ? $"{FormatBytes(item.BytesTransferred)} / {FormatBytes(item.TotalBytes)}"
                : "Waiting for data...";

            if (item.BytesPerSecond > 0 && !item.IsCompleted)
                detailsText += $" • {FormatBytes(item.BytesPerSecond)}/s";

            var cardStack = new StackLayout
            {
                Spacing = 6,
                Children =
                {
                    new Label
                    {
                        Text = item.FileName,
                        FontSize = 15,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = ThemeColors.TextPrimary
                    },
                    new Label
                    {
                        Text = item.DirectionLabel,
                        FontSize = 12,
                        TextColor = ThemeColors.TextSecondary
                    },
                    new Label
                    {
                        Text = item.Status,
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = statusColor
                    },
                    new ProgressBar
                    {
                        Progress = progress,
                        ProgressColor = statusColor,
                        BackgroundColor = ThemeColors.Divider,
                        HeightRequest = 8
                    },
                    new Label
                    {
                        Text = detailsText,
                        FontSize = 12,
                        TextColor = ThemeColors.TextSecondary
                    }
                }
            };

            if (!string.IsNullOrWhiteSpace(item.SavedPath) && item.IsSuccessful)
            {
                cardStack.Children.Add(new Label
                {
                    Text = $"Saved to: {item.SavedPath}",
                    FontSize = 11,
                    TextColor = ThemeColors.TextSecondary,
                    LineBreakMode = LineBreakMode.CharacterWrap
                });
            }

            if (!item.IsCompleted)
            {
                var cancelButton = new Button
                {
                    Text = "Cancel",
                    BackgroundColor = ThemeColors.DangerSoft,
                    TextColor = ThemeColors.Danger,
                    CornerRadius = 8,
                    HorizontalOptions = LayoutOptions.Start,
                    Padding = new Thickness(14, 8)
                };
                cancelButton.Clicked += async (_, _) => await CancelTransferAsync(item.TransferId);
                cardStack.Children.Add(cancelButton);
            }
            else if (item.IsSuccessful && !string.IsNullOrWhiteSpace(item.SavedPath) && File.Exists(item.SavedPath))
            {
                var openButton = new Button
                {
                    Text = "Open",
                    BackgroundColor = ThemeColors.SuccessBackground,
                    TextColor = ThemeColors.SuccessText,
                    CornerRadius = 8,
                    HorizontalOptions = LayoutOptions.Fill,
                    WidthRequest = 110
                };
                openButton.Clicked += async (_, _) => await OpenTransferAsync(item.SavedPath);

                var shareButton = new Button
                {
                    Text = "Share",
                    BackgroundColor = ThemeColors.InfoSoft,
                    TextColor = ThemeColors.InfoText,
                    CornerRadius = 8,
                    HorizontalOptions = LayoutOptions.Fill,
                    WidthRequest = 110
                };
                shareButton.Clicked += async (_, _) => await ShareTransferAsync(item.SavedPath);

                cardStack.Children.Add(new HorizontalStackLayout
                {
                    Spacing = 8,
                    Children = { openButton, shareButton }
                });
            }

            _transfersLayout.Children.Add(new Border
            {
                BackgroundColor = ThemeColors.PlaceholderBackground,
                Stroke = ThemeColors.CardBorder,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                Padding = new Thickness(12),
                Content = cardStack
            });
        }
    }

    private void OnConnectionStateChanged(object? sender, ClientConnectionState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            EnsureFileTransferService();
            UpdateConnectionUi();
            RefreshIncomingRequests();
            RefreshTransfers();
        });
    }

    private string GetReceiveDirectory()
    {
        var receiveDirectory = Path.Combine(FileSystem.Current.AppDataDirectory, "Transfers");
        Directory.CreateDirectory(receiveDirectory);
        return receiveDirectory;
    }

    private static string GetUniquePath(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = Path.Combine(directory, fileName);
        var counter = 1;

        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName}_{counter++}{extension}");
        }

        return candidate;
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(fileName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{value:0} {units[unitIndex]}" : $"{value:0.0} {units[unitIndex]}";
    }

    private sealed class TransferItem
    {
        public string TransferId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DirectionLabel { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public long BytesPerSecond { get; set; }
        public string? LocalPath { get; set; }
        public string? SavedPath { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsSuccessful { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
