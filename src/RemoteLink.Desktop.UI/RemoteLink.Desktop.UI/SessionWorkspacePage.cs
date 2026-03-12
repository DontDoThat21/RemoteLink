using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;

namespace RemoteLink.Desktop.UI;

/// <summary>
/// Tabbed workspace that hosts multiple simultaneous outgoing remote sessions.
/// </summary>
public sealed class SessionWorkspacePage : TabbedPage
{
    private readonly RemoteDesktopMultiSessionManager _sessionManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<string, Page> _sessionPages = new(StringComparer.OrdinalIgnoreCase);
    private string? _preferredSessionId;

    public SessionWorkspacePage(
        RemoteDesktopMultiSessionManager sessionManager,
        ILoggerFactory loggerFactory)
    {
        _sessionManager = sessionManager;
        _loggerFactory = loggerFactory;

        Title = "Remote Sessions";
        BarBackgroundColor = ThemeColors.HeaderBackground;
        BarTextColor = Colors.White;
        SelectedTabColor = Colors.White;
        UnselectedTabColor = ThemeColors.AccentText;

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
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _sessionManager.SessionsChanged -= OnSessionsChanged;
    }

    private void OnSessionsChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(RefreshTabs);
    }

    private void RefreshTabs()
    {
        var sessions = _sessionManager.GetSessions();
        var activeIds = sessions.Select(session => session.SessionId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var staleSessionId in _sessionPages.Keys.Where(id => !activeIds.Contains(id)).ToList())
        {
            if (_sessionPages.Remove(staleSessionId, out var page))
                Children.Remove(page);
        }

        foreach (var session in sessions)
        {
            if (_sessionPages.ContainsKey(session.SessionId))
                continue;

            var page = new RemoteViewerPage(
                session.Client,
                _loggerFactory.CreateLogger<RemoteViewerPage>(),
                () => _sessionManager.CloseSessionAsync(session.SessionId));

            page.Title = session.DisplayName;
            _sessionPages[session.SessionId] = page;
            Children.Add(page);
        }

        if (_sessionPages.Count == 0)
        {
            Children.Clear();
            Children.Add(BuildEmptyStatePage());
            return;
        }

        var emptyPage = Children.FirstOrDefault(page => page.AutomationId == "empty-session-workspace");
        if (emptyPage is not null)
            Children.Remove(emptyPage);

        if (!string.IsNullOrWhiteSpace(_preferredSessionId)
            && _sessionPages.TryGetValue(_preferredSessionId, out var preferredPage))
        {
            CurrentPage = preferredPage;
            _preferredSessionId = null;
        }
        else if (!Children.Contains(CurrentPage))
        {
            CurrentPage = Children.First();
        }
    }

    private ContentPage BuildEmptyStatePage()
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

        return new ContentPage
        {
            AutomationId = "empty-session-workspace",
            Title = "Sessions",
            BackgroundColor = ThemeColors.PageBackground,
            Content = new VerticalStackLayout
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
            }
        };
    }
}
