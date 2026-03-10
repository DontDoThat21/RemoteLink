using RemoteLink.Shared.Models;

namespace RemoteLink.Mobile;

/// <summary>
/// Mobile theme palette and theme application helper.
/// Provides a small shared color surface while task 6.13 is in progress.
/// </summary>
public static class ThemeColors
{
    private static bool _initialized;

    public static event Action? ThemeChanged;

    public static bool IsDark { get; private set; }

    public static void ApplyTheme(ThemeMode mode)
    {
        if (Application.Current is not null)
        {
            Application.Current.UserAppTheme = mode switch
            {
                ThemeMode.Light => AppTheme.Light,
                ThemeMode.Dark => AppTheme.Dark,
                _ => AppTheme.Unspecified
            };
        }

        var isDark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => Application.Current?.RequestedTheme == AppTheme.Dark
        };

        if (_initialized && IsDark == isDark)
            return;

        IsDark = isDark;
        _initialized = true;
        ThemeChanged?.Invoke();
    }

    public static Color Accent => Color.FromArgb("#512BD4");
    public static Color AccentSoft => IsDark ? Color.FromArgb("#2E2455") : Color.FromArgb("#F0EDFF");
    public static Color AccentText => IsDark ? Color.FromArgb("#DDD6FE") : Accent;
    public static Color PageBackground => IsDark ? Color.FromArgb("#111827") : Colors.White;
    public static Color CardBackground => IsDark ? Color.FromArgb("#1F2937") : Colors.White;
    public static Color CardBackgroundAlt => IsDark ? Color.FromArgb("#1A2332") : Color.FromArgb("#F8F6FF");
    public static Color SurfaceBackground => IsDark ? Color.FromArgb("#182231") : Color.FromArgb("#F5F3FF");
    public static Color SurfaceBackgroundAlt => IsDark ? Color.FromArgb("#1A2434") : Color.FromArgb("#FAFAFA");
    public static Color CardBorder => IsDark ? Color.FromArgb("#374151") : Color.FromArgb("#E0E0E0");
    public static Color SelectedCardBackground => IsDark ? Color.FromArgb("#2A2345") : Color.FromArgb("#F3E8FF");
    public static Color SelectedCardBorder => Accent;
    public static Color AddressBookBackground => IsDark ? Color.FromArgb("#2A261B") : Color.FromArgb("#FFFBF0");
    public static Color AddressBookBorder => IsDark ? Color.FromArgb("#6B5B2A") : Color.FromArgb("#E0D6B8");
    public static Color TextPrimary => IsDark ? Color.FromArgb("#F3F4F6") : Color.FromArgb("#333333");
    public static Color TextSecondary => IsDark ? Color.FromArgb("#D1D5DB") : Colors.Gray;
    public static Color TextMuted => IsDark ? Color.FromArgb("#9CA3AF") : Color.FromArgb("#888888");
    public static Color Divider => IsDark ? Color.FromArgb("#374151") : Color.FromArgb("#E0E0E0");
    public static Color InputBackground => IsDark ? Color.FromArgb("#243041") : Colors.White;
    public static Color Success => Color.FromArgb("#2E7D32");
    public static Color SuccessBackground => IsDark ? Color.FromArgb("#16281A") : Color.FromArgb("#E8F5E9");
    public static Color SuccessBorder => IsDark ? Color.FromArgb("#245C2B") : Color.FromArgb("#4CAF50");
    public static Color SuccessText => IsDark ? Color.FromArgb("#86EFAC") : Success;
    public static Color Danger => Color.FromArgb("#C62828");
    public static Color DangerSoft => IsDark ? Color.FromArgb("#3A1D1D") : Color.FromArgb("#FFE8E8");
    public static Color DangerText => IsDark ? Color.FromArgb("#FCA5A5") : Danger;
    public static Color Warning => Color.FromArgb("#EF6C00");
    public static Color WarningSoft => IsDark ? Color.FromArgb("#3A2A12") : Color.FromArgb("#FFF3E0");
    public static Color WarningText => IsDark ? Color.FromArgb("#FDBA74") : Warning;
    public static Color Info => Color.FromArgb("#1565C0");
    public static Color InfoSoft => IsDark ? Color.FromArgb("#1D2D3F") : Color.FromArgb("#E3F2FD");
    public static Color InfoText => IsDark ? Color.FromArgb("#93C5FD") : Info;
    public static Color SecondaryButtonBackground => IsDark ? Color.FromArgb("#2A3545") : Color.FromArgb("#EFEAFF");
    public static Color SecondaryButtonText => IsDark ? Color.FromArgb("#DDD6FE") : Accent;
    public static Color NeutralButtonBackground => IsDark ? Color.FromArgb("#4B5563") : Color.FromArgb("#BDBDBD");
    public static Color ToolbarBackground => IsDark ? Color.FromArgb("#182231") : Color.FromArgb("#F8F6FF");
    public static Color ToolbarBorder => IsDark ? Color.FromArgb("#4C4A68") : Color.FromArgb("#D7CCFF");
    public static Color ChipBackground => IsDark ? Color.FromArgb("#243041") : Color.FromArgb("#EFEAFF");
    public static Color ChipText => IsDark ? Color.FromArgb("#DDD6FE") : Accent;
    public static Color PlaceholderBackground => IsDark ? Color.FromArgb("#1A2434") : Color.FromArgb("#FAFAFA");
    public static Color ShadowColor => IsDark ? Color.FromArgb("#40000000") : Color.FromArgb("#20000000");
    public static Color TabBarBackground => IsDark ? Color.FromArgb("#111827") : Colors.White;
    public static Color TabBarUnselected => IsDark ? Color.FromArgb("#9CA3AF") : Color.FromArgb("#999999");

    public static (Color Background, Color Border, Color Text) GetQualityPalette(QualityRating? rating) => rating switch
    {
        QualityRating.Excellent => (SuccessBackground, SuccessBorder, SuccessText),
        QualityRating.Good => (InfoSoft, Info, InfoText),
        QualityRating.Fair => (WarningSoft, Warning, WarningText),
        QualityRating.Poor => (DangerSoft, Danger, DangerText),
        _ => (SurfaceBackgroundAlt, CardBorder, TextMuted)
    };
}