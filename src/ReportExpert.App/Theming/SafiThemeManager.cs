using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Wpf.Ui.Appearance;

namespace ReportExpert.App.Theming;

/// <summary>
/// Applies the SafiPay palette on top of WPF-UI. WPF-UI still draws the controls; this publishes the
/// <c>Safi.*</c> brushes used by the V3 shell and report center, sets the accent colour to SafiPay's primary,
/// and replaces the window background so the shell matches SafiPay in light and dark mode.
/// </summary>
public static class SafiThemeManager
{
    private static bool _initialized;

    public static bool IsDark { get; private set; }

    /// <summary>The selected theme name (see <c>ThemeNames</c>).</summary>
    public static string ThemeName { get; private set; } = ReportExpert.Common.ThemeNames.System;

    /// <summary>
    /// Applies a theme by name: picks WPF-UI's light or dark control base, then the matching REX palette.
    /// </summary>
    public static void ApplyTheme(string? themeName)
    {
        ThemeName = string.IsNullOrWhiteSpace(themeName) ? ReportExpert.Common.ThemeNames.System : themeName;
        switch (ReportExpert.Common.ThemeNames.IsDarkBase(ThemeName))
        {
            case null:
                ApplicationThemeManager.ApplySystemTheme();
                break;
            case true:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark, Wpf.Ui.Controls.WindowBackdropType.None);
                break;
            default:
                ApplicationThemeManager.Apply(ApplicationTheme.Light, Wpf.Ui.Controls.WindowBackdropType.None);
                break;
        }

        // WPF-UI raises Changed only when the base changes; switching Dark → Dusk keeps the base.
        Apply(ApplicationThemeManager.GetAppTheme());
    }

    public static event Action? PaletteChanged;

    /// <summary>Subscribes to WPF-UI theme changes and applies the palette for the current theme.</summary>
    public static void Initialize()
    {
        if (!_initialized)
        {
            _initialized = true;
            ApplicationThemeManager.Changed += (theme, _) => Apply(theme);
        }

        Apply(ApplicationThemeManager.GetAppTheme());
    }

    public static void Apply(ApplicationTheme theme)
    {
        var resources = System.Windows.Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        IsDark = theme == ApplicationTheme.Dark || theme == ApplicationTheme.HighContrast;
        var palette = SafiPalette.For(ThemeName, IsDark);
        foreach (var (name, color) in palette.Entries())
        {
            resources[$"Safi.{name}Color"] = color;
            resources[$"Safi.{name}Brush"] = Frozen(color);
        }

        // Translucent overlays used for hover and selection on the navy drawer and app bar.
        resources["Safi.OverlayHoverBrush"] = Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        resources["Safi.OverlaySelectedBrush"] = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        resources["Safi.SurfaceHoverBrush"] = Frozen(SafiPalette.Parse(palette.TableHover));
        resources["Safi.ChipBrush"] = Frozen(IsDark ? Color.FromRgb(0x24, 0x30, 0x3A) : Color.FromRgb(0xE7, 0xEE, 0xF4));

        // WPF-UI keys: the window background and the accent follow SafiPay.
        resources["ApplicationBackgroundBrush"] = Frozen(SafiPalette.Parse(palette.Background));
        resources["CardBackgroundFillColorDefaultBrush"] = Frozen(SafiPalette.Parse(palette.Surface));
        resources["CardStrokeColorDefaultBrush"] = Frozen(SafiPalette.Parse(palette.Divider));
        resources["TextFillColorPrimaryBrush"] = Frozen(SafiPalette.Parse(palette.TextPrimary));
        resources["TextFillColorSecondaryBrush"] = Frozen(SafiPalette.Parse(palette.TextSecondary));
        var accent = SafiPalette.Parse(IsDark ? palette.Primary : palette.PrimaryLighten);
        ApplicationAccentColorManager.Apply(accent, theme, systemGlassColor: false);

        PaletteChanged?.Invoke();
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
