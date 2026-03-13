using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.UI;

/// <summary>
/// Provides all UI colors based on the current theme (dark or light).
/// All pages reference these properties instead of hardcoded hex values.
/// Call <see cref="ApplyTheme"/> to switch and notify listeners.
/// </summary>
public static class ThemeColors
{
    /// <summary>Fired when the theme changes so pages can re-apply colors.</summary>
    public static event Action? ThemeChanged;

    /// <summary>Whether the current effective theme is dark.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>
    /// Resolves the effective theme from the setting and applies it.
    /// </summary>
    public static void ApplyTheme(ThemeMode mode)
    {
        bool dark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => IsSystemDark() // ThemeMode.System
        };

        if (IsDark == dark && _initialized) return;
        IsDark = dark;
        _initialized = true;
        ThemeChanged?.Invoke();
    }

    private static bool _initialized;

    private static bool IsSystemDark()
    {
        if (Application.Current is not null)
        {
            return Application.Current.RequestedTheme == AppTheme.Dark;
        }
        return false;
    }

    // ── Brand / Accent ───────────────────────────────────────────────────

    public static Color Accent => Color.FromArgb("#512BD4");
    public static Color AccentLight => Color.FromArgb("#E8E0FF");
    public static Color AccentText => Color.FromArgb("#D0C0FF");
    public static Color Danger => Color.FromArgb("#D32F2F");
    public static Color Success => Color.FromArgb("#4CAF50");
    public static Color Warning => Color.FromArgb("#FFA500");
    public static Color QualityGood => Color.FromArgb("#8BC34A");

    // ── Page backgrounds ─────────────────────────────────────────────────

    public static Color PageBackground => IsDark
        ? Color.FromArgb("#1A1A2E")
        : Color.FromArgb("#F5F5F5");

    public static Color CardBackground => IsDark
        ? Color.FromArgb("#252540")
        : Colors.White;

    public static Color CardBorder => IsDark
        ? Color.FromArgb("#3A3A5C")
        : Color.FromArgb("#D8D0F0");

    public static Color CardShadow => IsDark
        ? Color.FromArgb("#10000000")
        : Color.FromArgb("#20000000");

    // ── Header / Navigation bar ──────────────────────────────────────────

    public static Color HeaderBackground => IsDark
        ? Color.FromArgb("#0F0F23")
        : Color.FromArgb("#512BD4");

    public static Color NavBarBackground => HeaderBackground;

    // ── Status bar ───────────────────────────────────────────────────────

    public static Color StatusBarBackground => IsDark
        ? Color.FromArgb("#0D0D1A")
        : Color.FromArgb("#333333");

    // ── Text ─────────────────────────────────────────────────────────────

    public static Color TextPrimary => IsDark
        ? Color.FromArgb("#E8E8F0")
        : Color.FromArgb("#333333");

    public static Color TextSecondary => IsDark
        ? Color.FromArgb("#A0A0B8")
        : Color.FromArgb("#888888");

    public static Color TextMuted => IsDark
        ? Color.FromArgb("#707088")
        : Color.FromArgb("#AAAAAA");

    public static Color TextOnAccent => Colors.White;

    public static Color TextOnDark => Colors.White;

    public static Color TextVersion => IsDark
        ? Color.FromArgb("#606078")
        : Color.FromArgb("#999999");

    // ── Input fields ─────────────────────────────────────────────────────

    public static Color EntryBackground => IsDark
        ? Color.FromArgb("#2E2E48")
        : Colors.White;

    public static Color EntryText => TextPrimary;

    public static Color EntryPlaceholder => TextMuted;

    // ── Dividers / Separators ────────────────────────────────────────────

    public static Color Divider => IsDark
        ? Color.FromArgb("#3A3A5C")
        : Color.FromArgb("#E8E0FF");

    public static Color SeparatorLight => IsDark
        ? Color.FromArgb("#2E2E48")
        : Color.FromArgb("#EEEEEE");

    public static Color SectionSeparator => IsDark
        ? Color.FromArgb("#3A3A5C")
        : Color.FromArgb("#DDDDDD");

    // ── Buttons ──────────────────────────────────────────────────────────

    public static Color SecondaryButtonBackground => IsDark
        ? Color.FromArgb("#3A3A5C")
        : Color.FromArgb("#E8E0FF");

    public static Color SecondaryButtonText => IsDark
        ? Color.FromArgb("#D0C0FF")
        : Color.FromArgb("#512BD4");

    public static Color ResetButtonBackground => IsDark
        ? Color.FromArgb("#3A3A5C")
        : Color.FromArgb("#E0E0E0");

    public static Color ResetButtonText => IsDark
        ? Color.FromArgb("#E8E8F0")
        : Color.FromArgb("#333333");

    public static Color DisconnectTint => IsDark
        ? Color.FromArgb("#3D1A1A")
        : Color.FromArgb("#FFEAEA");

    // ── Session / connection indicators ──────────────────────────────────

    public static Color SessionRowBackground => IsDark
        ? Color.FromArgb("#2A2A44")
        : Color.FromArgb("#F8F8FF");

    public static Color ToolbarBackground => IsDark
        ? Color.FromArgb("#252540")
        : Color.FromArgb("#F8F5FF");

    public static Color ToolbarBorder => IsDark
        ? Color.FromArgb("#3A3A5C")
        : Color.FromArgb("#E0D8F8");

    public static Color MetricInactive => IsDark
        ? Color.FromArgb("#606078")
        : Color.FromArgb("#999999");

    public static Color QualityInactive => IsDark
        ? Color.FromArgb("#3A3A5C")
        : Color.FromArgb("#CCCCCC");

    // ── Settings sidebar ─────────────────────────────────────────────────

    public static Color SettingsSidebarBackground => IsDark
        ? Color.FromArgb("#0F0F23")
        : Color.FromArgb("#2D2D2D");

    public static Color SettingsNavInactive => IsDark
        ? Color.FromArgb("#A0A0B8")
        : Color.FromArgb("#CCCCCC");

    public static Color SettingsBottomBar => IsDark
        ? Color.FromArgb("#1A1A2E")
        : Color.FromArgb("#EEEEEE");

    // ── Chat ─────────────────────────────────────────────────────────────

    public static Color ChatBubbleRemote => IsDark
        ? Color.FromArgb("#2E2E48")
        : Colors.White;

    public static Color ChatBubbleRemoteBorder => IsDark
        ? Color.FromArgb("#3A3A5C")
        : Color.FromArgb("#E0E0E0");

    public static Color ChatInputBackground => IsDark
        ? Color.FromArgb("#252540")
        : Colors.White;

    // ── Remote viewer ────────────────────────────────────────────────────

    public static Color ViewerToolbar => IsDark
        ? Color.FromArgb("#0F0F23")
        : Color.FromArgb("#1A1A2E");

    public static Color ViewerStatusBar => Color.FromArgb("#0D0D1A");

    public static Color ViewerFpsText => IsDark
        ? Color.FromArgb("#A0A0B8")
        : Color.FromArgb("#CCCCCC");

    public static Color ViewerResolutionText => IsDark
        ? Color.FromArgb("#707088")
        : Color.FromArgb("#AAAAAA");

    // ── Connection status panel ──────────────────────────────────────────

    public static Color ConnectionPanelBackground => CardBackground;
    public static Color ConnectionPanelBorder => IsDark
        ? Color.FromArgb("#3A3A5C")
        : Color.FromArgb("#E0E0E0");

    // ── Partner panel ────────────────────────────────────────────────────

    public static Color PartnerSeparatorText => IsDark
        ? Color.FromArgb("#606078")
        : Color.FromArgb("#BBBBBB");

    public static Color ConnectionInfoText => IsDark
        ? Color.FromArgb("#A0A0B8")
        : Color.FromArgb("#666666");

    // ── Main sidebar navigation ───────────────────────────────────────────

    public static Color SidebarBackground => IsDark
        ? Color.FromArgb("#12122A")
        : Color.FromArgb("#F0EDF8");

    public static Color SidebarBorder => IsDark
        ? Color.FromArgb("#2A2A44")
        : Color.FromArgb("#DDD8EE");

    public static Color SidebarItemHover => IsDark
        ? Color.FromArgb("#2A2A44")
        : Color.FromArgb("#E8E0FF");

    public static Color SidebarItemActive => IsDark
        ? Color.FromArgb("#352F5C")
        : Color.FromArgb("#D8D0F0");

    public static Color SidebarIconInactive => IsDark
        ? Color.FromArgb("#707088")
        : Color.FromArgb("#888888");

    public static Color SidebarLabelInactive => IsDark
        ? Color.FromArgb("#707088")
        : Color.FromArgb("#888888");

    // ── Devices panel ─────────────────────────────────────────────────────

    public static Color AddressBookBackground => IsDark
        ? Color.FromArgb("#2A2844")
        : Color.FromArgb("#FFFBF0");

    public static Color AddressBookBorder => IsDark
        ? Color.FromArgb("#3E3A5C")
        : Color.FromArgb("#F0E0B0");

    public static Color SelectedCardBackground => IsDark
        ? Color.FromArgb("#2A3540")
        : Color.FromArgb("#F0F8FF");

    public static Color SelectedCardBorder => IsDark
        ? Color.FromArgb("#4080A0")
        : Color.FromArgb("#A0C8E0");

    public static Color ShadowColor => IsDark
        ? Color.FromArgb("#10000000")
        : Color.FromArgb("#20000000");

    // ── Files / Transfers ─────────────────────────────────────────────────

    public static Color SurfaceBackground => IsDark
        ? Color.FromArgb("#1E1E38")
        : Color.FromArgb("#FAFAFA");

    public static Color PlaceholderBackground => IsDark
        ? Color.FromArgb("#1E1E38")
        : Color.FromArgb("#FAFAFA");

    public static Color NeutralButtonBackground => IsDark
        ? Color.FromArgb("#4A4A64")
        : Color.FromArgb("#CCCCCC");

    // ── Status badges ─────────────────────────────────────────────────────

    public static Color SuccessBackground => IsDark
        ? Color.FromArgb("#1A3A1A")
        : Color.FromArgb("#E8F5E8");

    public static Color SuccessBorder => IsDark
        ? Color.FromArgb("#2A5A2A")
        : Color.FromArgb("#A8D8A8");

    public static Color SuccessText => IsDark
        ? Color.FromArgb("#6BCB6B")
        : Color.FromArgb("#2E7D32");

    public static Color DangerSoft => IsDark
        ? Color.FromArgb("#3D1A1A")
        : Color.FromArgb("#FFEAEA");

    public static Color DangerText => IsDark
        ? Color.FromArgb("#EF5350")
        : Color.FromArgb("#D32F2F");

    public static Color WarningSoft => IsDark
        ? Color.FromArgb("#3D3A1A")
        : Color.FromArgb("#FFF8E1");

    public static Color WarningText => IsDark
        ? Color.FromArgb("#FFB300")
        : Color.FromArgb("#F57F17");

    public static Color AccentSoft => IsDark
        ? Color.FromArgb("#2A2850")
        : Color.FromArgb("#EDE7FF");

    public static Color InputBackground => IsDark
        ? Color.FromArgb("#2E2E48")
        : Colors.White;

    public static Color CardBackgroundAlt => IsDark
        ? Color.FromArgb("#2A2A44")
        : Color.FromArgb("#F8F5FF");

    public static Color InfoSoft => IsDark
        ? Color.FromArgb("#1A2A3D")
        : Color.FromArgb("#E3F2FD");

    public static Color InfoText => IsDark
        ? Color.FromArgb("#42A5F5")
        : Color.FromArgb("#1565C0");

}
