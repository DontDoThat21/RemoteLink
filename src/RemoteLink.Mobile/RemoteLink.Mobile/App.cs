namespace RemoteLink.Mobile;

public partial class App : Application
{
    public App()
    {
        MainPage = new MainPage(null!); // Will be resolved via DI later
    }
}