namespace RemoteLink.Mobile;

/// <summary>
/// Files tab: file transfer UI (browse, send, receive, progress).
/// Backend exists in FileTransferService (Phase 4.1).
/// Full implementation planned in task 6.6.
/// </summary>
public class FilesPage : ContentPage
{
    public FilesPage()
    {
        Title = "Files";
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
                    Text = "\ud83d\udcc1",
                    FontSize = 48,
                    HorizontalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = "File Transfer",
                    FontSize = 22,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    TextColor = Color.FromArgb("#512BD4")
                },
                new Label
                {
                    Text = "Send and receive files between your devices.\nConnect to a desktop host first.",
                    FontSize = 14,
                    TextColor = Colors.Gray,
                    HorizontalTextAlignment = TextAlignment.Center,
                    HorizontalOptions = LayoutOptions.Center
                }
            }
        };
    }
}
