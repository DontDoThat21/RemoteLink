using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;

namespace RemoteLink.Desktop.UI;

/// <summary>
/// Custom-tabbed workspace that hosts multiple simultaneous outgoing remote sessions.
/// Uses a ContentPage with a manual tab bar instead of TabbedPage so it can be pushed
/// onto the NavigationPage stack on Windows without causing NavigationFailed.
/// </summary>
public sealed class SessionWorkspacePage : ContentPage
{
    private readonly RemoteDesktopMultiSessionManager _sessionManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<string, RemoteViewerPage> _viewerPages = new(StringComparer.OrdinalIgnoreCase);
    private string? _preferredSessionId;
    private string? _activeSessionId;

    private readonly HorizontalStackLayout _tabBar;
    private readonly ContentView _contentArea;

    public SessionWorkspacePage(
        RemoteDesktopMultiSessionManager sessionManager,
        ILoggerFactory loggerFactory)
    {
        _sessionManager = sessionManager;
        _loggerFactory = loggerFactory;

        Title = "Remote Sessions";
        BackgroundColor = ThemeColors.PageBackground;

        _tabBar = new HorizontalStackLayout { Spacing = 0 };

        var tabScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            Content = _tabBar,
            BackgroundColor = ThemeColors.HeaderBackground,
            HeightRequest = 42,
            VerticalOptions = LayoutOptions.Start,
        };

        _contentArea = new ContentView
        {
            BackgroundColor = Colors.Black,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            }
        };
        layout.Add(tabScroll, 0, 0);
        layout.Add(_contentArea, 0, 1);

        Content = layout;

        ToolbarItems.Add(new ToolbarItem("Dashboard", null, async () => await Navigation.PopAsync()));
    }

    public void FocusSession(string sessionId)
    {
        _preferredSessionId = sessionId;
        RefreshTabs();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _sessionManager.SessionsChanged += OnSessionsChanged;
        RefreshTabs();
        if (_activeSessionId is not null && _viewerPages.TryGetValue(_activeSessionId, out var page))
            page.StartViewing();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _sessionManager.SessionsChanged -= OnSessionsChanged;
        if (_activeSessionId is not null && _viewerPages.TryGetValue(_activeSessionId, out var page))
            page.StopViewing();
    }

    private void OnSessionsChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(RefreshTabs);

    private void RefreshTabs()
    {
        var sessions = _sessionManager.GetSessions();
        var activeIds = sessions.Select(s => s.SessionId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var staleId in _viewerPages.Keys.Where(id => !activeIds.Contains(id)).ToList())
        {
            if (staleId == _activeSessionId)
            {
                _viewerPages[staleId].StopViewing();
                _activeSessionId = null;
                _contentArea.Content = null;
            }
            _viewerPages.Remove(staleId);
        }

        foreach (var session in sessions)
        {
            if (_viewerPages.ContainsKey(session.SessionId))
                continue;

            var page = new RemoteViewerPage(
                session.Client,
                _loggerFactory.CreateLogger<RemoteViewerPage>(),
                () => _sessionManager.CloseSessionAsync(session.SessionId))
            {
                Title = session.DisplayName
            };
            _viewerPages[session.SessionId] = page;
        }

        if (_viewerPages.Count == 0)
        {
            _contentArea.Content = BuildEmptyStateView();
            RebuildTabBar();
            return;
        }

        string? targetId = null;
        if (!string.IsNullOrWhiteSpace(_preferredSessionId) && _viewerPages.ContainsKey(_preferredSessionId))
        {
            targetId = _preferredSessionId;
            _preferredSessionId = null;
        }
        else if (_activeSessionId is null || !_viewerPages.ContainsKey(_activeSessionId))
        {
            targetId = sessions.FirstOrDefault()?.SessionId;
        }

        RebuildTabBar();

        if (targetId is not null)
            ShowSession(targetId);
    }

    private void RebuildTabBar()
    {
        _tabBar.Children.Clear();
        foreach (var (sessionId, page) in _viewerPages)
        {
            var isActive = sessionId == _activeSessionId;
            var tabButton = new Button
            {
                Text = page.Title,
                BackgroundColor = isActive ? ThemeColors.Accent : ThemeColors.SecondaryButtonBackground,
                TextColor = isActive ? Colors.White : ThemeColors.TextPrimary,
                FontSize = 13,
                CornerRadius = 0,
                Padding = new Thickness(16, 0),
                HeightRequest = 42,
                BorderWidth = 0,
                Margin = new Thickness(0, 0, 1, 0),
            };
            var capturedId = sessionId;
            tabButton.Clicked += (_, _) => ShowSession(capturedId);
            _tabBar.Children.Add(tabButton);
        }
    }

    private void ShowSession(string sessionId)
    {
        if (sessionId == _activeSessionId)
            return;

        if (_activeSessionId is not null && _viewerPages.TryGetValue(_activeSessionId, out var oldPage))
            oldPage.StopViewing();

        _activeSessionId = sessionId;

        if (_viewerPages.TryGetValue(sessionId, out var newPage))
        {
            _contentArea.Content = newPage.Content;
            newPage.StartViewing();
        }

        RebuildTabBar();
    }

    private View BuildEmptyStateView()
    {
        var backButton = new Button
        {
            Text = "Return to Dashboard",
            BackgroundColor = ThemeColors.Accent,
            TextColor = Colors.White,
            CornerRadius = 8,
            HorizontalOptions = LayoutOptions.Center,
            Padding = new Thickness(16, 10)
        };
        backButton.Clicked += async (_, _) => await Navigation.PopAsync();

        return new VerticalStackLayout
        {
            Padding = new Thickness(24),
            Spacing = 16,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = "No active remote sessions",
                    FontSize = 22,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = ThemeColors.TextPrimary,
                    HorizontalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = "Open another partner connection from the dashboard to create a new tabbed session.",
                    FontSize = 14,
                    TextColor = ThemeColors.TextSecondary,
                    HorizontalTextAlignment = TextAlignment.Center,
                    MaximumWidthRequest = 380
                },
                backButton
            }
        };
    }
}