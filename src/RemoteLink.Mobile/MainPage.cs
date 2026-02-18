using Microsoft.Maui.Controls;

namespace RemoteLink.Mobile;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        // Placeholder UI - actual implementation needs platform-specific code
        Content = new VerticalStackLayout {
    Margin = 20,
    Children = {
        new Label { Text = "RemoteLink Mobile", FontSize = 36, HorizontalOptions = LayoutOptions.Center },
        new Button {
            Text = "Connect to Host",
            Clicked = OnConnectClicked
        }
    }
};
    }
}
