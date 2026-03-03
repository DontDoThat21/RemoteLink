namespace RemoteLink.Mobile;

/// <summary>
/// Chat tab: in-session text messaging with the connected desktop host.
/// Backend exists in MessagingService (Phase 4.6).
/// Full implementation planned in task 6.7.
/// </summary>
public class MobileChatPage : ContentPage
{
    public MobileChatPage()
    {
        Title = "Chat";
        BackgroundColor = Colors.White;

        Content = new StackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Spacing = 16,
            Padding = new Thickness(32),
            Children =
            {
                new Label
                {
                    Text = "\ud83d\udcac",
                    FontSize = 48,
                    HorizontalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = "Chat",
                    FontSize = 22,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    TextColor = Color.FromArgb("#512BD4")
                },
                new Label
                {
                    Text = "Send messages to the connected desktop host.\nConnect to a device first to start chatting.",
                    FontSize = 14,
                    TextColor = Colors.Gray,
                    HorizontalTextAlignment = TextAlignment.Center,
                    HorizontalOptions = LayoutOptions.Center
                }
            }
        };
    }
}
