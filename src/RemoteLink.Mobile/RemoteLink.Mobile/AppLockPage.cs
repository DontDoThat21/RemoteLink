using RemoteLink.Mobile.Services;

namespace RemoteLink.Mobile;

public class AppLockPage : ContentPage
{
    private readonly IAppLockService _appLockService;
    private Entry _pinEntry = null!;
    private Label _statusLabel = null!;
    private Button _unlockButton = null!;

    public AppLockPage(IAppLockService appLockService)
    {
        _appLockService = appLockService;
        Title = "Unlock";
        BuildLayout();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ThemeColors.ThemeChanged += OnThemeChanged;
        BuildLayout();
        _pinEntry.Focus();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ThemeColors.ThemeChanged -= OnThemeChanged;
    }

    protected override bool OnBackButtonPressed() => true;

    private void OnThemeChanged()
    {
        MainThread.BeginInvokeOnMainThread(BuildLayout);
    }

    private void BuildLayout()
    {
        BackgroundColor = ThemeColors.PageBackground;

        _pinEntry = new Entry
        {
            Placeholder = "6-digit PIN",
            Keyboard = Keyboard.Numeric,
            IsPassword = true,
            MaxLength = 6,
            BackgroundColor = ThemeColors.InputBackground,
            TextColor = ThemeColors.TextPrimary,
            HeightRequest = 48
        };

        _statusLabel = new Label
        {
            IsVisible = false,
            FontSize = 13,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = ThemeColors.Danger
        };

        _unlockButton = new Button
        {
            Text = "Unlock",
            BackgroundColor = ThemeColors.NeutralButtonBackground,
            TextColor = Colors.White,
            CornerRadius = 10,
            HeightRequest = 48,
            IsEnabled = false
        };

        _pinEntry.TextChanged += (_, _) =>
        {
            _unlockButton.IsEnabled = (_pinEntry.Text?.Length ?? 0) == 6;
            _unlockButton.BackgroundColor = _unlockButton.IsEnabled ? ThemeColors.Accent : ThemeColors.NeutralButtonBackground;
        };
        _pinEntry.Completed += OnUnlockClicked;
        _unlockButton.Clicked += OnUnlockClicked;

        Content = new Grid
        {
            Padding = new Thickness(24),
            Children =
            {
                new Border
                {
                    BackgroundColor = ThemeColors.CardBackgroundAlt,
                    Stroke = ThemeColors.CardBorder,
                    StrokeThickness = 1,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
                    Padding = new Thickness(20),
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    MaximumWidthRequest = 420,
                    Content = new VerticalStackLayout
                    {
                        Spacing = 14,
                        Children =
                        {
                            new Label
                            {
                                Text = "🔒 RemoteLink Locked",
                                FontSize = 24,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = ThemeColors.Accent,
                                HorizontalTextAlignment = TextAlignment.Center
                            },
                            new Label
                            {
                                Text = "Enter your 6-digit app PIN to unlock saved devices and the mobile client.",
                                FontSize = 13,
                                TextColor = ThemeColors.TextSecondary,
                                HorizontalTextAlignment = TextAlignment.Center
                            },
                            _pinEntry,
                            _statusLabel,
                            _unlockButton
                        }
                    }
                }
            }
        };
    }

    private async void OnUnlockClicked(object? sender, EventArgs e)
    {
        var pin = _pinEntry.Text?.Trim() ?? string.Empty;
        if (pin.Length != 6)
            return;

        var success = await _appLockService.VerifyPinAsync(pin);
        if (success)
        {
            await Navigation.PopModalAsync(false);
            return;
        }

        _pinEntry.Text = string.Empty;
        _statusLabel.Text = "Incorrect PIN. Try again.";
        _statusLabel.IsVisible = true;
        _unlockButton.IsEnabled = false;
        _unlockButton.BackgroundColor = ThemeColors.NeutralButtonBackground;
    }
}