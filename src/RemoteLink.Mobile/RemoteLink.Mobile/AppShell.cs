using RemoteLink.Mobile.Services;

namespace RemoteLink.Mobile;

/// <summary>
/// Shell-based navigation with bottom tabs: Connect, Devices, Files, Chat, Settings.
/// </summary>
public class AppShell : Shell
{
    private readonly MobileChatSession _chatSession;
    private readonly ShellContent _chatTab;

    public AppShell(MobileChatSession chatSession)
    {
        _chatSession = chatSession;

        // Shell styling
        Shell.SetFlyoutBehavior(this, FlyoutBehavior.Disabled);
        Shell.SetTabBarBackgroundColor(this, Color.FromArgb("#FFFFFF"));
        Shell.SetTabBarTitleColor(this, Color.FromArgb("#512BD4"));
        Shell.SetTabBarUnselectedColor(this, Color.FromArgb("#999999"));

        var tabBar = new TabBar();

        tabBar.Items.Add(CreateTab<ConnectPage>("Connect", "connect_icon", "Connect"));
        tabBar.Items.Add(CreateTab<DevicesPage>("Devices", "devices_icon", "Devices"));
        tabBar.Items.Add(CreateTab<FilesPage>("Files", "files_icon", "Files"));
        _chatTab = CreateTab<MobileChatPage>("Chat", "chat_icon", "Chat");
        tabBar.Items.Add(_chatTab);
        tabBar.Items.Add(CreateTab<MobileSettingsPage>("Settings", "settings_icon", "Settings"));

        Items.Add(tabBar);

        // Register routes for non-tab pages (navigated to via Navigation.PushAsync)
        Routing.RegisterRoute("RecentConnections", typeof(RecentConnectionsPage));

        _chatSession.UnreadCountChanged += OnUnreadCountChanged;
        UpdateChatTabTitle();
    }

    private void OnUnreadCountChanged(object? sender, int unreadCount)
    {
        MainThread.BeginInvokeOnMainThread(UpdateChatTabTitle);
    }

    private void UpdateChatTabTitle()
    {
        var unreadCount = _chatSession.UnreadCount;
        _chatTab.Title = unreadCount > 0 ? $"Chat ({unreadCount})" : "Chat";
    }

    private static ShellContent CreateTab<TPage>(string title, string icon, string route)
        where TPage : ContentPage
    {
        var content = new ShellContent
        {
            Title = title,
            Route = route,
            ContentTemplate = new DataTemplate(typeof(TPage))
        };

        // Use Unicode symbols as tab icons (cross-platform, no image files needed)
        // The icon property requires an ImageSource; we use FontImageSource for symbols
        content.Icon = new FontImageSource
        {
            Glyph = title switch
            {
                "Connect"  => "\ud83d\udd17", // link symbol
                "Devices"  => "\ud83d\udda5", // desktop symbol
                "Files"    => "\ud83d\udcc1", // folder symbol
                "Chat"     => "\ud83d\udcac", // speech bubble
                "Settings" => "\u2699",       // gear symbol
                _          => "\u2022"
            },
            FontFamily = null, // uses system default
            Size = 22,
            Color = Color.FromArgb("#512BD4")
        };

        return content;
    }
}
