namespace RemoteLink.Desktop.UI;

public partial class App : Application
{
    private readonly MainPage _mainPage;

    public App(MainPage mainPage)
    {
        _mainPage = mainPage;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new NavigationPage(_mainPage)
        {
            BarBackgroundColor = Color.FromArgb("#512BD4"),
            BarTextColor = Colors.White
        });

        window.Title = "RemoteLink Desktop";
        window.Width = 520;
        window.Height = 680;
        window.MinimumWidth = 420;
        window.MinimumHeight = 560;

        return window;
    }
}
