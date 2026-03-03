namespace RemoteLink.Mobile;

/// <summary>
/// Settings tab: display quality, input preferences, notifications, theme.
/// Full implementation planned in task 6.12.
/// </summary>
public class MobileSettingsPage : ContentPage
{
    public MobileSettingsPage()
    {
        Title = "Settings";
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
                    Text = "\u2699",
                    FontSize = 48,
                    HorizontalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = "Settings",
                    FontSize = 22,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    TextColor = Color.FromArgb("#512BD4")
                },
                new Label
                {
                    Text = "Configure display quality, input sensitivity,\naudio streaming, and notifications.",
                    FontSize = 14,
                    TextColor = Colors.Gray,
                    HorizontalTextAlignment = TextAlignment.Center,
                    HorizontalOptions = LayoutOptions.Center
                },
                BuildInfoSection()
            }
        };
    }

    private static View BuildInfoSection()
    {
        return new StackLayout
        {
            Spacing = 8,
            Margin = new Thickness(0, 24, 0, 0),
            Children =
            {
                new BoxView
                {
                    Color = Color.FromArgb("#E0E0E0"),
                    HeightRequest = 1,
                    HorizontalOptions = LayoutOptions.Fill
                },
                new Label
                {
                    Text = "RemoteLink Mobile v1.0",
                    FontSize = 12,
                    TextColor = Colors.Gray,
                    HorizontalTextAlignment = TextAlignment.Center,
                    HorizontalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = "Free, open-source remote desktop solution",
                    FontSize = 11,
                    TextColor = Colors.Gray,
                    HorizontalTextAlignment = TextAlignment.Center,
                    HorizontalOptions = LayoutOptions.Center
                }
            }
        };
    }
}
