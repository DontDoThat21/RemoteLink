using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Mobile;

/// <summary>
/// Mobile settings page for display, input, notification, and audio preferences.
/// </summary>
public class MobileSettingsPage : ContentPage
{
    private readonly IAppSettingsService _settingsService;

    private Switch _adaptiveQualitySwitch = null!;
    private Slider _qualitySlider = null!;
    private Label _qualityValueLabel = null!;
    private Picker _imageFormatPicker = null!;
    private Slider _gestureSensitivitySlider = null!;
    private Label _gestureSensitivityValueLabel = null!;
    private Switch _notificationsSwitch = null!;
    private Switch _audioSwitch = null!;
    private Label _feedbackLabel = null!;

    public MobileSettingsPage(IAppSettingsService settingsService)
    {
        _settingsService = settingsService;

        Title = "Settings";
        BackgroundColor = Colors.White;
        Content = BuildLayout();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            await _settingsService.LoadAsync();
            PopulateControls(_settingsService.Current);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Settings", $"Failed to load settings: {ex.Message}", "OK");
        }
    }

    private View BuildLayout()
    {
        _adaptiveQualitySwitch = new Switch { OnColor = Color.FromArgb("#512BD4") };
        _qualitySlider = new Slider { Minimum = 50, Maximum = 85, ThumbColor = Color.FromArgb("#512BD4"), MinimumTrackColor = Color.FromArgb("#512BD4") };
        _qualityValueLabel = BuildValueLabel();
        _imageFormatPicker = new Picker { Title = "Preferred image format" };
        _imageFormatPicker.Items.Add("JPEG");
        _imageFormatPicker.Items.Add("PNG");

        _gestureSensitivitySlider = new Slider { Minimum = 0.5, Maximum = 2.0, ThumbColor = Color.FromArgb("#512BD4"), MinimumTrackColor = Color.FromArgb("#512BD4") };
        _gestureSensitivityValueLabel = BuildValueLabel();

        _notificationsSwitch = new Switch { OnColor = Color.FromArgb("#512BD4") };
        _audioSwitch = new Switch { OnColor = Color.FromArgb("#512BD4") };

        _qualitySlider.ValueChanged += (_, e) => _qualityValueLabel.Text = $"{Math.Round(e.NewValue):0}%";
        _gestureSensitivitySlider.ValueChanged += (_, e) => _gestureSensitivityValueLabel.Text = $"{e.NewValue:0.00}×";

        _feedbackLabel = new Label
        {
            IsVisible = false,
            FontSize = 12,
            TextColor = Color.FromArgb("#2E7D32"),
            VerticalOptions = LayoutOptions.Center
        };

        var saveButton = new Button
        {
            Text = "Save",
            BackgroundColor = Color.FromArgb("#512BD4"),
            TextColor = Colors.White,
            CornerRadius = 10,
            HeightRequest = 44
        };
        saveButton.Clicked += OnSaveClicked;

        var resetButton = new Button
        {
            Text = "Reset",
            BackgroundColor = Color.FromArgb("#EFEAFF"),
            TextColor = Color.FromArgb("#512BD4"),
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
                    TextColor = Color.FromArgb("#512BD4")
                },
                new Label
                {
                    Text = "Tune connection quality, touch sensitivity, notifications, and audio behavior.",
                    FontSize = 13,
                    TextColor = Colors.Gray
                },
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
            settings.Display.EnableAdaptiveQuality = _adaptiveQualitySwitch.IsToggled;
            settings.Display.ImageQuality = (int)Math.Round(_qualitySlider.Value);
            settings.Display.ImageFormat = _imageFormatPicker.SelectedIndex == 1
                ? RemoteLink.Shared.Models.ImageFormat.Png
                : RemoteLink.Shared.Models.ImageFormat.Jpeg;
            settings.Input.GestureSensitivity = Math.Round(_gestureSensitivitySlider.Value, 2);
            settings.General.ShowConnectionNotifications = _notificationsSwitch.IsToggled;
            settings.Audio.EnableAudio = _audioSwitch.IsToggled;

            await _settingsService.SaveAsync();
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
            ShowFeedback("Settings reset to defaults.");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Settings", $"Failed to reset settings: {ex.Message}", "OK");
        }
    }

    private void PopulateControls(AppSettings settings)
    {
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

    private void ShowFeedback(string message)
    {
        _feedbackLabel.Text = message;
        _feedbackLabel.IsVisible = true;
    }

    private static Label BuildValueLabel() => new()
    {
        FontSize = 12,
        FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#512BD4"),
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
            TextColor = Color.FromArgb("#512BD4")
        });
        stack.Add(new Label
        {
            Text = subtitle,
            FontSize = 12,
            TextColor = Colors.Gray
        });

        foreach (var child in children)
            stack.Add(child);

        return new Border
        {
            BackgroundColor = Color.FromArgb("#F8F6FF"),
            Stroke = Color.FromArgb("#D7CCFF"),
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
                    TextColor = Colors.Black
                },
                new Label
                {
                    Text = description,
                    FontSize = 12,
                    TextColor = Colors.Gray
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
                    Color = Color.FromArgb("#E0E0E0"),
                    HeightRequest = 1,
                    HorizontalOptions = LayoutOptions.Fill
                },
                new Label
                {
                    Text = "RemoteLink Mobile v1.0",
                    FontSize = 12,
                    TextColor = Colors.Gray,
                    HorizontalTextAlignment = TextAlignment.Center
                },
                new Label
                {
                    Text = "Display, input, audio, and notification preferences are stored per device.",
                    FontSize = 11,
                    TextColor = Colors.Gray,
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
        };
    }
}
