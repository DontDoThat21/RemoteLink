using RemoteLink.Mobile.Services;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Mobile;

/// <summary>
/// Mobile settings page for display, input, notification, and audio preferences.
/// </summary>
public class MobileSettingsPage : ContentPage
{
    private readonly IAppSettingsService _settingsService;
    private readonly IAppLockService _appLockService;

    private Picker _themePicker = null!;
    private Switch _appLockSwitch = null!;
    private Stepper _appLockTimeoutStepper = null!;
    private Label _appLockTimeoutValueLabel = null!;
    private Label _pinStatusLabel = null!;
    private Button _setPinButton = null!;
    private Button _removePinButton = null!;
    private Switch _adaptiveQualitySwitch = null!;
    private Slider _qualitySlider = null!;
    private Label _qualityValueLabel = null!;
    private Picker _imageFormatPicker = null!;
    private Slider _gestureSensitivitySlider = null!;
    private Label _gestureSensitivityValueLabel = null!;
    private Switch _notificationsSwitch = null!;
    private Switch _audioSwitch = null!;
    private Label _feedbackLabel = null!;

    public MobileSettingsPage(IAppSettingsService settingsService, IAppLockService appLockService)
    {
        _settingsService = settingsService;
        _appLockService = appLockService;

        Title = "Settings";
        RefreshTheme();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        ThemeColors.ThemeChanged += OnThemeChanged;
        RefreshTheme();

        try
        {
            await _settingsService.LoadAsync();
            PopulateControls(_settingsService.Current);
            await _appLockService.InitializeAsync();
            await RefreshAppLockUiAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Settings", $"Failed to load settings: {ex.Message}", "OK");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ThemeColors.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshTheme();
            PopulateControls(_settingsService.Current);
            _ = RefreshAppLockUiAsync();
        });
    }

    private void RefreshTheme()
    {
        BackgroundColor = ThemeColors.PageBackground;
        Content = BuildLayout();
    }

    private View BuildLayout()
    {
        _themePicker = new Picker { Title = "Theme" };
        _themePicker.Items.Add("System");
        _themePicker.Items.Add("Light");
        _themePicker.Items.Add("Dark");

        _appLockSwitch = new Switch { OnColor = ThemeColors.Accent };
        _appLockTimeoutStepper = new Stepper { Minimum = 0, Maximum = 60, Increment = 1 };
        _appLockTimeoutValueLabel = BuildValueLabel();
        _appLockTimeoutStepper.ValueChanged += (_, e) => _appLockTimeoutValueLabel.Text = FormatTimeout((int)e.NewValue);
        _pinStatusLabel = new Label
        {
            FontSize = 12,
            TextColor = ThemeColors.TextSecondary
        };
        _setPinButton = new Button
        {
            BackgroundColor = ThemeColors.SecondaryButtonBackground,
            TextColor = ThemeColors.SecondaryButtonText,
            CornerRadius = 8,
            HeightRequest = 40
        };
        _setPinButton.Clicked += OnSetPinClicked;
        _removePinButton = new Button
        {
            Text = "Remove PIN",
            BackgroundColor = ThemeColors.DangerSoft,
            TextColor = ThemeColors.Danger,
            CornerRadius = 8,
            HeightRequest = 40
        };
        _removePinButton.Clicked += OnRemovePinClicked;

        _adaptiveQualitySwitch = new Switch { OnColor = ThemeColors.Accent };
        _qualitySlider = new Slider { Minimum = 50, Maximum = 85, ThumbColor = ThemeColors.Accent, MinimumTrackColor = ThemeColors.Accent };
        _qualityValueLabel = BuildValueLabel();
        _imageFormatPicker = new Picker { Title = "Preferred image format" };
        _imageFormatPicker.Items.Add("JPEG");
        _imageFormatPicker.Items.Add("PNG");

        _gestureSensitivitySlider = new Slider { Minimum = 0.5, Maximum = 2.0, ThumbColor = ThemeColors.Accent, MinimumTrackColor = ThemeColors.Accent };
        _gestureSensitivityValueLabel = BuildValueLabel();

        _notificationsSwitch = new Switch { OnColor = ThemeColors.Accent };
        _audioSwitch = new Switch { OnColor = ThemeColors.Accent };

        _qualitySlider.ValueChanged += (_, e) => _qualityValueLabel.Text = $"{Math.Round(e.NewValue):0}%";
        _gestureSensitivitySlider.ValueChanged += (_, e) => _gestureSensitivityValueLabel.Text = $"{e.NewValue:0.00}×";

        _feedbackLabel = new Label
        {
            IsVisible = false,
            FontSize = 12,
            TextColor = ThemeColors.Success,
            VerticalOptions = LayoutOptions.Center
        };

        var saveButton = new Button
        {
            Text = "Save",
            BackgroundColor = ThemeColors.Accent,
            TextColor = Colors.White,
            CornerRadius = 10,
            HeightRequest = 44
        };
        saveButton.Clicked += OnSaveClicked;

        var resetButton = new Button
        {
            Text = "Reset",
            BackgroundColor = ThemeColors.SecondaryButtonBackground,
            TextColor = ThemeColors.SecondaryButtonText,
            CornerRadius = 10,
            HeightRequest = 44
        };
        resetButton.Clicked += OnResetClicked;

        var content = new VerticalStackLayout
        {
            Spacing = 16,
            Padding = new Thickness(16, 16, 16, 32),
            Children =
            {
                new Label
                {
                    Text = "Mobile Settings",
                    FontSize = 24,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = ThemeColors.Accent
                },
                new Label
                {
                    Text = "Tune appearance, connection quality, touch sensitivity, notifications, and audio behavior.",
                    FontSize = 13,
                    TextColor = ThemeColors.TextSecondary
                },
                BuildCard(
                    "General",
                    "App-wide appearance preferences.",
                    BuildPickerRow("Theme", "Follow the OS theme or force light/dark mode.", _themePicker)),
                BuildCard(
                    "Security",
                    "Protect the mobile client and saved devices with a local PIN app lock.",
                    BuildSettingRow("App lock", "Require your local app PIN when returning to the app.", _appLockSwitch),
                    BuildStepperRow("Lock timeout", "Minutes the app can stay in the background before it locks again. Use 0 to lock immediately.", _appLockTimeoutStepper, _appLockTimeoutValueLabel),
                    new VerticalStackLayout
                    {
                        Spacing = 8,
                        Children =
                        {
                            BuildSettingText("App lock PIN", "Set, change, or remove the 6-digit PIN used to unlock the app."),
                            _pinStatusLabel,
                            new Grid
                            {
                                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
                                ColumnSpacing = 12,
                                Children = { _setPinButton, CreateGridChild(_removePinButton, 1) }
                            }
                        }
                    }),
                BuildCard(
                    "Display",
                    "Remote session quality preferences.",
                    BuildSettingRow("Adaptive quality", "Let the host adjust image quality automatically.", _adaptiveQualitySwitch),
                    BuildSliderRow("Preferred quality", "Used when adaptive quality is turned off.", _qualitySlider, _qualityValueLabel),
                    BuildPickerRow("Image format", "Request JPEG or PNG frames from the host for active sessions.", _imageFormatPicker)),
                BuildCard(
                    "Input",
                    "Touch and gesture tuning for the remote viewer.",
                    BuildSliderRow("Gesture sensitivity", "Scales pan and scroll gestures in the remote viewer.", _gestureSensitivitySlider, _gestureSensitivityValueLabel)),
                BuildCard(
                    "Notifications",
                    "In-app alerts for connection lifecycle events.",
                    BuildSettingRow("Connection notifications", "Show alerts when a session connects or disconnects.", _notificationsSwitch)),
                BuildCard(
                    "Audio",
                    "Remote audio streaming preference.",
                    BuildSettingRow("Enable audio", "Enable or disable host audio streaming for the current device.", _audioSwitch)),
                _feedbackLabel,
                new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Star)
                    },
                    ColumnSpacing = 12,
                    Children =
                    {
                        saveButton,
                        CreateGridChild(resetButton, 1)
                    }
                },
                BuildInfoSection()
            }
        };

        return new ScrollView { Content = content };
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        try
        {
            var settings = _settingsService.Current;
            settings.General.Theme = _themePicker.SelectedIndex switch
            {
                1 => ThemeMode.Light,
                2 => ThemeMode.Dark,
                _ => ThemeMode.System
            };
            if (_appLockSwitch.IsToggled && !await _appLockService.HasPinAsync())
            {
                var pinConfigured = await PromptForPinAsync("Set App Lock PIN", "Create a 6-digit PIN to unlock RemoteLink on this device.");
                if (!pinConfigured)
                    return;
            }

            settings.Security.EnableAppLock = _appLockSwitch.IsToggled;
            settings.Security.AppLockTimeoutMinutes = (int)_appLockTimeoutStepper.Value;
            settings.Display.EnableAdaptiveQuality = _adaptiveQualitySwitch.IsToggled;
            settings.Display.ImageQuality = (int)Math.Round(_qualitySlider.Value);
            settings.Display.ImageFormat = _imageFormatPicker.SelectedIndex == 1
                ? RemoteLink.Shared.Models.ImageFormat.Png
                : RemoteLink.Shared.Models.ImageFormat.Jpeg;
            settings.Input.GestureSensitivity = Math.Round(_gestureSensitivitySlider.Value, 2);
            settings.General.ShowConnectionNotifications = _notificationsSwitch.IsToggled;
            settings.Audio.EnableAudio = _audioSwitch.IsToggled;

            await _settingsService.SaveAsync();
            await _appLockService.InitializeAsync();
            await RefreshAppLockUiAsync();
            ShowFeedback("Settings saved.");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Settings", $"Failed to save settings: {ex.Message}", "OK");
        }
    }

    private async void OnResetClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlertAsync(
            "Reset settings",
            "Restore the mobile settings to their default values?",
            "Reset",
            "Cancel");

        if (!confirmed)
            return;

        try
        {
            await _settingsService.ResetAsync();
            PopulateControls(_settingsService.Current);
            await _appLockService.InitializeAsync();
            await RefreshAppLockUiAsync();
            ShowFeedback("Settings reset to defaults.");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Settings", $"Failed to reset settings: {ex.Message}", "OK");
        }
    }

    private void PopulateControls(AppSettings settings)
    {
        _themePicker.SelectedIndex = settings.General.Theme switch
        {
            ThemeMode.Light => 1,
            ThemeMode.Dark => 2,
            _ => 0
        };
        _appLockSwitch.IsToggled = settings.Security.EnableAppLock;
        _appLockTimeoutStepper.Value = Math.Clamp(settings.Security.AppLockTimeoutMinutes, _appLockTimeoutStepper.Minimum, _appLockTimeoutStepper.Maximum);
        _appLockTimeoutValueLabel.Text = FormatTimeout((int)_appLockTimeoutStepper.Value);
        _adaptiveQualitySwitch.IsToggled = settings.Display.EnableAdaptiveQuality;
        _qualitySlider.Value = Math.Clamp(settings.Display.ImageQuality, _qualitySlider.Minimum, _qualitySlider.Maximum);
        _qualityValueLabel.Text = $"{Math.Round(_qualitySlider.Value):0}%";
        _imageFormatPicker.SelectedIndex = settings.Display.ImageFormat == RemoteLink.Shared.Models.ImageFormat.Png ? 1 : 0;

        _gestureSensitivitySlider.Value = Math.Clamp(settings.Input.GestureSensitivity, _gestureSensitivitySlider.Minimum, _gestureSensitivitySlider.Maximum);
        _gestureSensitivityValueLabel.Text = $"{_gestureSensitivitySlider.Value:0.00}×";

        _notificationsSwitch.IsToggled = settings.General.ShowConnectionNotifications;
        _audioSwitch.IsToggled = settings.Audio.EnableAudio;
        _feedbackLabel.IsVisible = false;
    }

    private async Task RefreshAppLockUiAsync()
    {
        var hasPin = await _appLockService.HasPinAsync();
        _pinStatusLabel.Text = hasPin ? "PIN configured for this device." : "No app lock PIN configured yet.";
        _pinStatusLabel.TextColor = hasPin ? ThemeColors.SuccessText : ThemeColors.TextSecondary;
        _setPinButton.Text = hasPin ? "Change PIN" : "Set PIN";
        _removePinButton.IsEnabled = hasPin;
        _removePinButton.Opacity = hasPin ? 1.0 : 0.55;
    }

    private async void OnSetPinClicked(object? sender, EventArgs e)
    {
        if (await PromptForPinAsync("Set App Lock PIN", "Enter a new 6-digit app PIN."))
            ShowFeedback("App lock PIN updated.");
    }

    private async void OnRemovePinClicked(object? sender, EventArgs e)
    {
        if (!await _appLockService.HasPinAsync())
            return;

        var confirmed = await DisplayAlertAsync("Remove App Lock PIN", "Disable the local app PIN on this device?", "Remove", "Cancel");
        if (!confirmed)
            return;

        await _appLockService.ClearPinAsync();
        _appLockSwitch.IsToggled = false;
        await RefreshAppLockUiAsync();
        ShowFeedback("App lock PIN removed.");
    }

    private async Task<bool> PromptForPinAsync(string title, string message)
    {
        var firstPin = await DisplayPromptAsync(title, message, "Save", "Cancel", "123456", maxLength: 6, keyboard: Keyboard.Numeric);
        if (!IsValidPin(firstPin))
            return false;

        var confirmPin = await DisplayPromptAsync(title, "Confirm the same 6-digit PIN.", "Confirm", "Cancel", "123456", maxLength: 6, keyboard: Keyboard.Numeric);
        if (!string.Equals(firstPin, confirmPin, StringComparison.Ordinal))
        {
            await DisplayAlertAsync("App Lock PIN", "The PIN entries did not match.", "OK");
            return false;
        }

        await _appLockService.SetPinAsync(firstPin!);
        await RefreshAppLockUiAsync();
        return true;
    }

    private static bool IsValidPin(string? pin) => !string.IsNullOrWhiteSpace(pin) && pin.Length == 6 && pin.All(char.IsDigit);

    private static string FormatTimeout(int minutes) => minutes == 0 ? "Immediate" : $"{minutes} min";

    private void ShowFeedback(string message)
    {
        _feedbackLabel.Text = message;
        _feedbackLabel.IsVisible = true;
    }

    private static Label BuildValueLabel() => new()
    {
        FontSize = 12,
        FontAttributes = FontAttributes.Bold,
        TextColor = ThemeColors.Accent,
        HorizontalOptions = LayoutOptions.End,
        VerticalOptions = LayoutOptions.Center
    };

    private static Border BuildCard(string title, string subtitle, params View[] children)
    {
        var stack = new VerticalStackLayout { Spacing = 12 };
        stack.Add(new Label
        {
            Text = title,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeColors.Accent
        });
        stack.Add(new Label
        {
            Text = subtitle,
            FontSize = 12,
            TextColor = ThemeColors.TextSecondary
        });

        foreach (var child in children)
            stack.Add(child);

        return new Border
        {
            BackgroundColor = ThemeColors.CardBackgroundAlt,
            Stroke = ThemeColors.CardBorder,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(16),
            Content = stack
        };
    }

    private static View BuildSettingRow(string title, string description, View trailing)
    {
        var grid = CreateTwoColumnGrid();
        grid.Add(BuildSettingText(title, description));
        grid.Add(CreateGridChild(trailing, 1));
        return grid;
    }

    private static View BuildPickerRow(string title, string description, Picker picker)
    {
        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                BuildSettingText(title, description),
                picker
            }
        };
    }

    private static View BuildSliderRow(string title, string description, Slider slider, Label valueLabel)
    {
        var header = CreateTwoColumnGrid();
        header.Add(BuildSettingText(title, description));
        header.Add(CreateGridChild(valueLabel, 1));

        return new VerticalStackLayout
        {
            Spacing = 6,
            Children = { header, slider }
        };
    }

    private static View BuildStepperRow(string title, string description, Stepper stepper, Label valueLabel)
    {
        var header = CreateTwoColumnGrid();
        header.Add(BuildSettingText(title, description));
        header.Add(CreateGridChild(valueLabel, 1));

        return new VerticalStackLayout
        {
            Spacing = 6,
            Children = { header, stepper }
        };
    }

    private static View BuildSettingText(string title, string description)
    {
        return new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label
                {
                    Text = title,
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = ThemeColors.TextPrimary
                },
                new Label
                {
                    Text = description,
                    FontSize = 12,
                    TextColor = ThemeColors.TextSecondary
                }
            }
        };
    }

    private static Grid CreateTwoColumnGrid() => new()
    {
        ColumnDefinitions =
        {
            new ColumnDefinition(GridLength.Star),
            new ColumnDefinition(GridLength.Auto)
        },
        ColumnSpacing = 12
    };

    private static View CreateGridChild(View view, int column)
    {
        Grid.SetColumn(view, column);
        return view;
    }

    private static View BuildInfoSection()
    {
        return new VerticalStackLayout
        {
            Spacing = 6,
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                new BoxView
                {
                    Color = ThemeColors.Divider,
                    HeightRequest = 1,
                    HorizontalOptions = LayoutOptions.Fill
                },
                new Label
                {
                    Text = "RemoteLink Mobile v1.0",
                    FontSize = 12,
                    TextColor = ThemeColors.TextSecondary,
                    HorizontalTextAlignment = TextAlignment.Center
                },
                new Label
                {
                    Text = "Display, input, audio, and notification preferences are stored per device.",
                    FontSize = 11,
                    TextColor = ThemeColors.TextMuted,
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
        };
    }
}
