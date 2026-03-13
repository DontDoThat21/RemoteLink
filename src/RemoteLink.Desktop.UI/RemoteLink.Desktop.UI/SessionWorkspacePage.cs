using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;

namespace RemoteLink.Desktop.UI;

/// <summary>
/// Lists active outgoing remote sessions and navigates to their individual viewer pages
/// using standard MAUI navigation (PushAsync).  This avoids the MAUI single-parent
/// constraint that fires when embedding a ContentPage's Content view into another page,
/// which was causing the WinUI Frame.NavigationFailed / COMException.
/// </summary>
public sealed class SessionWorkspacePage : ContentPage
{
    private readonly RemoteDesktopMultiSessionManager _sessionManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<string, RemoteViewerPage> _viewerPages = new(StringComparer.OrdinalIgnoreCase);
    private string? _preferredSessionId;
    private bool _hasAutoNavigated;

    private readonly VerticalStackLayout _sessionListLayout;

    public SessionWorkspacePage(
        RemoteDesktopMultiSessionManager sessionManager,
        ILoggerFactory loggerFactory)
    {
        _sessionManager = sessionManager;
        _loggerFactory = loggerFactory;

        Title = "Remote Sessions";
        BackgroundColor = ThemeColors.PageBackground;

        _sessionListLayout = new VerticalStackLayout
        {
            Spacing = 10,
            Padding = new Thickness(20),
        };

        Content = new ScrollView { Content = _sessionListLayout };

        ToolbarItems.Add(new ToolbarItem("Dashboard", null, async () => await Navigation.PopAsync()));
    }

    /// <summary>
    /// Stores the session ID to auto-navigate to when this page first appears.
    /// Safe to call before the page is pushed onto the navigation stack.
    /// </summary>
    public void FocusSession(string sessionId) => _preferredSessionId = sessionId;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _sessionManager.SessionsChanged += OnSessionsChanged;
        RefreshSessions();
        RebuildSessionList();

        // Auto-navigate to the target session the first time this page appears.
        // On subsequent appearances (returning from a viewer) just show the list.
        if (_hasAutoNavigated)
            return;

        _hasAutoNavigated = true;

        var targetId = _preferredSessionId ?? _viewerPages.Keys.FirstOrDefault();
        _preferredSessionId = null;

        if (targetId is not null && _viewerPages.TryGetValue(targetId, out var page))
            await Navigation.PushAsync(page);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _sessionManager.SessionsChanged -= OnSessionsChanged;
    }

    private void OnSessionsChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshSessions();
            RebuildSessionList();
        });

    private void RefreshSessions()
    {
        var sessions = _sessionManager.GetSessions();
        var activeIds = sessions.Select(s => s.SessionId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove stale viewer pages whose sessions have ended
        foreach (var staleId in _viewerPages.Keys.Where(id => !activeIds.Contains(id)).ToList())
            _viewerPages.Remove(staleId);

        // Create viewer pages for newly connected sessions
        foreach (var session in sessions)
        {
            if (_viewerPages.ContainsKey(session.SessionId))
                continue;

            // closeSessionAsync is null: RemoteViewerPage handles its own disconnect
            // by calling Navigation.PopAsync(), which is correct for stack-based navigation.
            // The MultiSessionManager's own ConnectionStateChanged handler cleans up the session.
            var page = new RemoteViewerPage(
                session.Client,
                _loggerFactory.CreateLogger<RemoteViewerPage>(),
                closeSessionAsync: null)
            {
                Title = session.DisplayName
            };
            _viewerPages[session.SessionId] = page;
        }
    }

    private void RebuildSessionList()
    {
        _sessionListLayout.Children.Clear();

        if (_viewerPages.Count == 0)
        {
            _sessionListLayout.Children.Add(new Label
            {
                Text = "No active remote sessions",
                FontSize = 20,
                FontAttributes = FontAttributes.Bold,
                TextColor = ThemeColors.TextPrimary,
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 48, 0, 12),
            });
            _sessionListLayout.Children.Add(new Label
            {
                Text = "All sessions have ended. Return to the dashboard to connect again.",
                FontSize = 14,
                TextColor = ThemeColors.TextSecondary,
                HorizontalTextAlignment = TextAlignment.Center,
                MaximumWidthRequest = 400,
                HorizontalOptions = LayoutOptions.Center,
            });
            return;
        }

        _sessionListLayout.Children.Add(new Label
        {
            Text = "Active Sessions",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.TextPrimary,
            Margin = new Thickness(0, 0, 0, 4),
        });

        foreach (var (sessionId, page) in _viewerPages)
        {
            var nameLabel = new Label
            {
                Text = page.Title,
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = ThemeColors.TextPrimary,
                VerticalOptions = LayoutOptions.Center,
            };

            var openButton = new Button
            {
                Text = "Open",
                BackgroundColor = ThemeColors.Accent,
                TextColor = Colors.White,
                CornerRadius = 6,
                Padding = new Thickness(18, 0),
                HeightRequest = 36,
                FontSize = 13,
                VerticalOptions = LayoutOptions.Center,
            };

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
                ColumnSpacing = 12,
            };
            row.Add(nameLabel, 0, 0);
            row.Add(openButton, 1, 0);

            var card = new Border
            {
                BackgroundColor = ThemeColors.CardBackground,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                StrokeThickness = 0,
                Padding = new Thickness(16, 14),
                Content = row,
            };

            var capturedId = sessionId;
            openButton.Clicked += async (_, _) =>
            {
                if (_viewerPages.TryGetValue(capturedId, out var viewerPage))
                    await Navigation.PushAsync(viewerPage);
            };

            _sessionListLayout.Children.Add(card);
        }
    }
}