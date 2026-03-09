using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Mobile;

/// <summary>
/// Scrollable list of past connection sessions showing date, duration, device name, and outcome.
/// Navigated to from the Devices tab via a "View History" button.
/// </summary>
public class RecentConnectionsPage : ContentPage
{
    private readonly ILogger<RecentConnectionsPage> _logger;
    private readonly IConnectionHistoryService _historyService;

    private StackLayout _recordListLayout = null!;
    private Label _emptyLabel = null!;
    private bool _loaded;

    public RecentConnectionsPage(ILogger<RecentConnectionsPage> logger, IConnectionHistoryService historyService)
    {
        _logger = logger;
        _historyService = historyService;

        Title = "Recent Connections";
        BackgroundColor = Colors.White;

        Content = BuildLayout();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_loaded)
        {
            await _historyService.LoadAsync();
            _loaded = true;
        }

        RefreshList();
    }

    private View BuildLayout()
    {
        var root = new StackLayout
        {
            Padding = new Thickness(16),
            Spacing = 12
        };

        // Header
        root.Add(new Label
        {
            Text = "Recent Connections",
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#512BD4"),
            Margin = new Thickness(0, 8, 0, 0)
        });

        root.Add(new Label
        {
            Text = "Your past remote desktop sessions",
            FontSize = 13,
            TextColor = Colors.Gray,
            Margin = new Thickness(0, 0, 0, 4)
        });

        // Clear history button
        var clearButton = new Button
        {
            Text = "Clear History",
            FontSize = 13,
            BackgroundColor = Color.FromArgb("#FFE8E8"),
            TextColor = Color.FromArgb("#C62828"),
            CornerRadius = 6,
            HeightRequest = 36,
            HorizontalOptions = LayoutOptions.End,
            Padding = new Thickness(12, 0)
        };
        clearButton.Clicked += OnClearHistoryClicked;
        root.Add(clearButton);

        // Separator
        root.Add(new BoxView
        {
            Color = Color.FromArgb("#E0E0E0"),
            HeightRequest = 1,
            HorizontalOptions = LayoutOptions.Fill
        });

        // Empty state
        _emptyLabel = new Label
        {
            Text = "No connection history yet.\nConnect to a remote host to see your sessions here.",
            FontSize = 14,
            TextColor = Colors.Gray,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 40, 0, 0)
        };

        _recordListLayout = new StackLayout { Spacing = 8 };
        _recordListLayout.Add(_emptyLabel);
        root.Add(_recordListLayout);

        return new ScrollView { Content = root };
    }

    private void RefreshList()
    {
        _recordListLayout.Clear();

        var records = _historyService.GetAll();
        if (records.Count == 0)
        {
            _recordListLayout.Add(_emptyLabel);
            return;
        }

        // Group by date
        var grouped = records.GroupBy(r => r.ConnectedAt.ToLocalTime().Date);

        foreach (var group in grouped)
        {
            // Date header
            var dateText = group.Key == DateTime.Today
                ? "Today"
                : group.Key == DateTime.Today.AddDays(-1)
                    ? "Yesterday"
                    : group.Key.ToString("MMM d, yyyy");

            _recordListLayout.Add(new Label
            {
                Text = dateText,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#666666"),
                Margin = new Thickness(0, 8, 0, 4)
            });

            foreach (var record in group)
                _recordListLayout.Add(BuildRecordCard(record));
        }
    }

    private View BuildRecordCard(ConnectionRecord record)
    {
        var (outcomeColor, outcomeText) = record.Outcome switch
        {
            ConnectionOutcome.Success => (Color.FromArgb("#4CAF50"), "Connected"),
            ConnectionOutcome.Failed => (Color.FromArgb("#C62828"), "Failed"),
            ConnectionOutcome.Disconnected => (Color.FromArgb("#FF8F00"), "Disconnected"),
            ConnectionOutcome.Error => (Color.FromArgb("#C62828"), "Error"),
            _ => (Colors.Gray, "Unknown")
        };

        var card = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#DADCE0"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14),
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(Color.FromArgb("#20000000")),
                Offset = new Point(0, 2),
                Radius = 6
            }
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),   // icon
                new ColumnDefinition(GridLength.Star),    // info
                new ColumnDefinition(GridLength.Auto),    // outcome badge
            },
            ColumnSpacing = 10,
            VerticalOptions = LayoutOptions.Center
        };

        // Clock icon
        var icon = new Label
        {
            Text = "\ud83d\udd52", // clock emoji
            FontSize = 24,
            VerticalOptions = LayoutOptions.Center
        };
        Grid.SetColumn(icon, 0);

        // Info column
        var infoStack = new StackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };

        infoStack.Add(new Label
        {
            Text = record.DeviceName,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.Black
        });

        infoStack.Add(new Label
        {
            Text = $"{record.IPAddress}:{record.Port}",
            FontSize = 12,
            TextColor = Colors.Gray
        });

        // Time
        infoStack.Add(new Label
        {
            Text = record.ConnectedAt.ToLocalTime().ToString("h:mm tt"),
            FontSize = 12,
            TextColor = Color.FromArgb("#888888")
        });

        // Duration
        if (record.Duration.TotalSeconds >= 1)
        {
            var durationText = record.Duration.TotalHours >= 1
                ? record.Duration.ToString(@"h\:mm\:ss")
                : record.Duration.ToString(@"m\:ss");

            infoStack.Add(new Label
            {
                Text = $"Duration: {durationText}",
                FontSize = 11,
                TextColor = Color.FromArgb("#888888")
            });
        }

        Grid.SetColumn(infoStack, 1);

        // Outcome badge
        var badge = new Border
        {
            BackgroundColor = outcomeColor.WithAlpha(0.15f),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
            Stroke = Colors.Transparent,
            Padding = new Thickness(8, 4),
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = outcomeText,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = outcomeColor
            }
        };
        Grid.SetColumn(badge, 2);

        grid.Add(icon);
        grid.Add(infoStack);
        grid.Add(badge);
        card.Content = grid;

        return card;
    }

    private async void OnClearHistoryClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync(
            "Clear History",
            "Remove all connection history? This cannot be undone.",
            "Clear", "Cancel");

        if (!confirm) return;

        await _historyService.ClearAsync();
        RefreshList();
    }
}
