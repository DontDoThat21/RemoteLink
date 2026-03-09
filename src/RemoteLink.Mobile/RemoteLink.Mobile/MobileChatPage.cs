using Microsoft.Extensions.Logging;
using RemoteLink.Mobile.Services;
using RemoteLink.Shared.Models;

namespace RemoteLink.Mobile;

/// <summary>
/// Chat tab: in-session text messaging with the connected desktop host.
/// </summary>
public class MobileChatPage : ContentPage
{
    private readonly MobileChatSession _chatSession;
    private readonly ILogger<MobileChatPage> _logger;
    private readonly Label _statusLabel;
    private readonly Label _unreadBadgeLabel;
    private readonly Label _emptyStateLabel;
    private readonly ScrollView _scrollView;
    private readonly StackLayout _messageList;
    private readonly Entry _messageEntry;
    private readonly Button _sendButton;

    public MobileChatPage(MobileChatSession chatSession, ILogger<MobileChatPage> logger)
    {
        _chatSession = chatSession;
        _logger = logger;

        Title = "Chat";
        BackgroundColor = Colors.White;

        _statusLabel = new Label
        {
            FontSize = 14,
            TextColor = Colors.Gray
        };

        _unreadBadgeLabel = new Label
        {
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#512BD4"),
            Padding = new Thickness(10, 4),
            HorizontalOptions = LayoutOptions.End,
            IsVisible = false
        };

        _messageList = new StackLayout
        {
            Spacing = 10,
            Padding = new Thickness(0, 8)
        };

        _emptyStateLabel = new Label
        {
            Text = "No messages yet. Say hello to the connected host.",
            FontSize = 14,
            TextColor = Colors.Gray,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 24, 0, 0)
        };

        _scrollView = new ScrollView
        {
            Content = _messageList
        };

        _messageEntry = new Entry
        {
            Placeholder = "Type a message...",
            BackgroundColor = Colors.White,
            ReturnType = ReturnType.Send,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing
        };
        _messageEntry.TextChanged += OnMessageTextChanged;
        _messageEntry.Completed += OnSendClicked;

        _sendButton = new Button
        {
            Text = "Send",
            BackgroundColor = Color.FromArgb("#512BD4"),
            TextColor = Colors.White,
            CornerRadius = 8,
            WidthRequest = 88,
            IsEnabled = false
        };
        _sendButton.Clicked += OnSendClicked;

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            Padding = new Thickness(16),
            Children =
            {
                CreateGridChild(BuildHeader(), row: 0),
                CreateGridChild(BuildMessageArea(), row: 1),
                CreateGridChild(BuildComposer(), row: 2)
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _chatSession.SessionStateChanged += OnSessionStateChanged;
        _chatSession.MessagesChanged += OnMessagesChanged;
        _chatSession.UnreadCountChanged += OnUnreadCountChanged;

        RefreshUi();
        await _chatSession.MarkAllAsReadAsync();
        RefreshUi();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        _chatSession.SessionStateChanged -= OnSessionStateChanged;
        _chatSession.MessagesChanged -= OnMessagesChanged;
        _chatSession.UnreadCountChanged -= OnUnreadCountChanged;
    }

    private View BuildHeader()
    {
        return new Border
        {
            BackgroundColor = Color.FromArgb("#F8F6FF"),
            Stroke = Color.FromArgb("#DDD6FE"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(16, 14),
            Margin = new Thickness(0, 0, 0, 12),
            Content = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                },
                Children =
                {
                    new Label
                    {
                        Text = "💬 Chat",
                        FontSize = 22,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#512BD4")
                    },
                    CreateGridChild(_unreadBadgeLabel, column: 1),
                    CreateGridChild(_statusLabel, row: 1)
                }
            }
        };
    }

    private View BuildMessageArea()
    {
        return new Border
        {
            BackgroundColor = Color.FromArgb("#FAFAFA"),
            Stroke = Color.FromArgb("#E5E7EB"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(14, 8),
            Content = new Grid
            {
                Children =
                {
                    _scrollView,
                    _emptyStateLabel
                }
            }
        };
    }

    private View BuildComposer()
    {
        var composerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10,
            Children =
            {
                _messageEntry,
                CreateGridChild(_sendButton, column: 1)
            }
        };

        return new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E5E7EB"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(12),
            Margin = new Thickness(0, 12, 0, 0),
            Content = composerGrid
        };
    }

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(RefreshUi);
    }

    private void OnMessagesChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            RefreshUi();
            await _chatSession.MarkAllAsReadAsync();
            RefreshUi();
        });
    }

    private void OnUnreadCountChanged(object? sender, int unreadCount)
    {
        MainThread.BeginInvokeOnMainThread(UpdateUnreadBadge);
    }

    private void RefreshUi()
    {
        _statusLabel.Text = _chatSession.HasActiveSession
            ? $"Connected to {_chatSession.ConnectedHostName}"
            : "Connect to a desktop host to start chatting.";

        _messageEntry.IsEnabled = _chatSession.HasActiveSession;
        RefreshMessages();
        UpdateUnreadBadge();
        UpdateSendButtonState();
    }

    private void RefreshMessages()
    {
        _messageList.Children.Clear();

        var messages = _chatSession.GetMessages();
        foreach (var message in messages)
        {
            _messageList.Children.Add(BuildMessageBubble(message));
        }

        _emptyStateLabel.IsVisible = messages.Count == 0;

        if (messages.Count > 0)
        {
            _ = _scrollView.ScrollToAsync(0, double.MaxValue, false);
        }
    }

    private View BuildMessageBubble(ChatMessage message)
    {
        var isLocal = _chatSession.IsLocalMessage(message);

        var bubbleStack = new StackLayout { Spacing = 3 };

        bubbleStack.Add(new Label
        {
            Text = message.SenderName,
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = isLocal ? Color.FromArgb("#D9CCFF") : Color.FromArgb("#6B7280")
        });

        bubbleStack.Add(new Label
        {
            Text = message.Text,
            FontSize = 15,
            TextColor = isLocal ? Colors.White : Color.FromArgb("#111827")
        });

        bubbleStack.Add(new Label
        {
            Text = message.Timestamp.ToLocalTime().ToString("HH:mm"),
            FontSize = 10,
            HorizontalOptions = LayoutOptions.End,
            TextColor = isLocal ? Color.FromArgb("#D9CCFF") : Color.FromArgb("#9CA3AF")
        });

        var bubble = new Border
        {
            BackgroundColor = isLocal ? Color.FromArgb("#512BD4") : Colors.White,
            Stroke = isLocal ? Colors.Transparent : Color.FromArgb("#E5E7EB"),
            StrokeThickness = isLocal ? 0 : 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(12, 10),
            MaximumWidthRequest = 300,
            Content = bubbleStack
        };

        return new StackLayout
        {
            HorizontalOptions = isLocal ? LayoutOptions.End : LayoutOptions.Start,
            Margin = new Thickness(0, 0, 0, 4),
            Children = { bubble }
        };
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        var text = _messageEntry.Text?.Trim();
        if (!_chatSession.HasActiveSession || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _messageEntry.Text = string.Empty;

        try
        {
            await _chatSession.SendMessageAsync(text);
            RefreshUi();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send chat message");
            await DisplayAlertAsync("Chat Error", $"Failed to send message: {ex.Message}", "OK");
        }
    }

    private void OnMessageTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateSendButtonState();
    }

    private void UpdateSendButtonState()
    {
        _sendButton.IsEnabled = _chatSession.HasActiveSession && !string.IsNullOrWhiteSpace(_messageEntry.Text);
    }

    private void UpdateUnreadBadge()
    {
        var unreadCount = _chatSession.UnreadCount;
        _unreadBadgeLabel.Text = unreadCount > 0 ? $"{unreadCount} unread" : string.Empty;
        _unreadBadgeLabel.IsVisible = unreadCount > 0;
    }

    private static View CreateGridChild(View view, int column = 0, int row = 0)
    {
        Grid.SetColumn(view, column);
        Grid.SetRow(view, row);
        return view;
    }
}
