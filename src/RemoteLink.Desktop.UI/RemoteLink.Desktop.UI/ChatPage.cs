using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.UI;

/// <summary>
/// In-session chat page — shows message history and allows sending messages to the remote party.
/// </summary>
public class ChatPage : ContentPage
{
    private readonly IMessagingService _messaging;
    private readonly ILogger<ChatPage> _logger;
    private readonly ScrollView _scrollView;
    private readonly StackLayout _messageList;
    private readonly Entry _messageEntry;
    private readonly string _localName;

    public ChatPage(IMessagingService messaging, ILogger<ChatPage> logger)
    {
        _messaging = messaging;
        _logger = logger;
        _localName = Environment.MachineName;

        Title = "Chat";
        BackgroundColor = Color.FromArgb("#F5F5F5");

        _messageList = new StackLayout
        {
            Spacing = 8,
            Padding = new Thickness(12, 8)
        };

        _scrollView = new ScrollView
        {
            Content = _messageList,
            VerticalOptions = LayoutOptions.Fill
        };

        _messageEntry = new Entry
        {
            Placeholder = "Type a message...",
            HorizontalOptions = LayoutOptions.Fill,
            ReturnType = ReturnType.Send,
            TextColor = Color.FromArgb("#333333"),
            PlaceholderColor = Color.FromArgb("#AAAAAA"),
        };
        _messageEntry.Completed += OnSendClicked;

        var sendButton = new Button
        {
            Text = "Send",
            BackgroundColor = Color.FromArgb("#512BD4"),
            TextColor = Colors.White,
            CornerRadius = 6,
            WidthRequest = 70,
            HeightRequest = 40,
        };
        sendButton.Clicked += OnSendClicked;

        var inputRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 8,
            Padding = new Thickness(12, 8),
            BackgroundColor = Colors.White,
            Children =
            {
                _messageEntry,
                CreateGridChild(sendButton, column: 1),
            }
        };

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
            Children =
            {
                _scrollView,
                CreateGridChild(inputRow, row: 1),
            }
        };

        _messaging.MessageReceived += OnMessageReceived;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshMessages();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _messaging.MessageReceived -= OnMessageReceived;
        // Mark all unread messages as read when leaving the chat page
        foreach (var msg in _messaging.GetMessages().Where(m => !m.IsRead))
            _ = _messaging.MarkAsReadAsync(msg.MessageId);
    }

    private void OnMessageReceived(object? sender, ChatMessage message)
    {
        MainThread.BeginInvokeOnMainThread(RefreshMessages);
    }

    private void RefreshMessages()
    {
        _messageList.Children.Clear();
        var messages = _messaging.GetMessages();

        if (messages.Count == 0)
        {
            _messageList.Children.Add(new Label
            {
                Text = "No messages yet. Say hello!",
                FontSize = 13,
                TextColor = Color.FromArgb("#AAAAAA"),
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 20)
            });
            return;
        }

        foreach (var msg in messages)
            _messageList.Children.Add(BuildMessageBubble(msg));

        _ = _scrollView.ScrollToAsync(0, double.MaxValue, animated: false);
    }

    private View BuildMessageBubble(ChatMessage message)
    {
        bool isLocal = message.SenderName == _localName;

        var bubble = new Border
        {
            Padding = new Thickness(10, 6),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            BackgroundColor = isLocal ? Color.FromArgb("#512BD4") : Colors.White,
            Stroke = isLocal ? Colors.Transparent : Color.FromArgb("#E0E0E0"),
            StrokeThickness = 1,
            MaximumWidthRequest = 300,
            Content = new StackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label
                    {
                        Text = message.SenderName,
                        FontSize = 10,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = isLocal ? Color.FromArgb("#D0C0FF") : Color.FromArgb("#888888"),
                    },
                    new Label
                    {
                        Text = message.Text,
                        FontSize = 13,
                        TextColor = isLocal ? Colors.White : Color.FromArgb("#333333"),
                    },
                    new Label
                    {
                        Text = message.Timestamp.ToLocalTime().ToString("HH:mm"),
                        FontSize = 9,
                        TextColor = isLocal ? Color.FromArgb("#C0B0FF") : Color.FromArgb("#AAAAAA"),
                        HorizontalOptions = LayoutOptions.End,
                    }
                }
            }
        };

        return new StackLayout
        {
            HorizontalOptions = isLocal ? LayoutOptions.End : LayoutOptions.Start,
            Children = { bubble }
        };
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        var text = _messageEntry.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        _messageEntry.Text = "";

        try
        {
            await _messaging.SendMessageAsync(text);
            MainThread.BeginInvokeOnMainThread(RefreshMessages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send chat message");
            await DisplayAlertAsync("Error", $"Failed to send: {ex.Message}", "OK");
        }
    }

    private static View CreateGridChild(View view, int column = 0, int row = 0)
    {
        Grid.SetColumn(view, column);
        Grid.SetRow(view, row);
        return view;
    }
}
