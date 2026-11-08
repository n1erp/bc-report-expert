using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ReportExpert.App.Theming;

/// <summary>
/// The SafiPay design palette (see <c>SafiPay.Web/Components/SafiTheme.cs</c>), carried over to WPF so REX V3
/// shares SafiPay's colours: a deep navy app bar and drawer, a teal secondary, calm grey backgrounds and
/// 10px rounded surfaces.
/// </summary>
public sealed record SafiPalette(
    string Primary,
    string PrimaryDarken,
    string PrimaryLighten,
    string PrimaryContrastText,
    string Secondary,
    string SecondaryLighten,
    string SecondaryContrastText,
    string Tertiary,
    string Info,
    string Success,
    string Warning,
    string Error,
    string Background,
    string Surface,
    string AppBarBackground,
    string AppBarText,
    string DrawerBackground,
    string DrawerText,
    string DrawerIcon,
    string DrawerSelected,
    string TextPrimary,
    string TextSecondary,
    string Divider,
    string TableLines,
    string TableHover)
{
    public static SafiPalette Light { get; } = new(
        Primary: "#0F3D5E",
        PrimaryDarken: "#0A2C45",
        PrimaryLighten: "#1D5680",
        PrimaryContrastText: "#FFFFFF",
        Secondary: "#1F7A6A",
        SecondaryLighten: "#2C9A86",
        SecondaryContrastText: "#FFFFFF",
        Tertiary: "#8A5A12",
        Info: "#0F5F8A",
        Success: "#1B7F4E",
        Warning: "#B86E00",
        Error: "#B42318",
        Background: "#F3F6F8",
        Surface: "#FFFFFF",
        AppBarBackground: "#0F3D5E",
        AppBarText: "#FFFFFF",
        DrawerBackground: "#0F3D5E",
        DrawerText: "#E7EEF4",
        DrawerIcon: "#D5E3EE",
        DrawerSelected: "#1D5680",
        TextPrimary: "#1C2833",
        TextSecondary: "#5C6B7A",
        Divider: "#D5DEE6",
        TableLines: "#E4EAF0",
        TableHover: "#F7FAFC");

    public static SafiPalette Dark { get; } = new(
        Primary: "#8EC3E6",
        PrimaryDarken: "#6AA8D0",
        PrimaryLighten: "#B7D9F0",
        PrimaryContrastText: "#0E1A24",
        Secondary: "#8ED4C8",
        SecondaryLighten: "#C5EBE4",
        SecondaryContrastText: "#0E1A24",
        Tertiary: "#E2B56A",
        Info: "#8EC3E6",
        Success: "#7DCEA0",
        Warning: "#F0C36A",
        Error: "#F0A8A0",
        Background: "#12181E",
        Surface: "#1C242C",
        AppBarBackground: "#161D24",
        AppBarText: "#E8EEF3",
        DrawerBackground: "#10161C",
        DrawerText: "#E7EEF4",
        DrawerIcon: "#D5E3EE",
        DrawerSelected: "#24303A",
        TextPrimary: "#E8EEF3",
        TextSecondary: "#A8B3BD",
        Divider: "#2C3844",
        TableLines: "#2C3844",
        TableHover: "#24303A");

    /// <summary>Ocean-inspired: deep teal surfaces, cyan accent, sea-green secondary.</summary>
    public static SafiPalette Aquatic { get; } = new(
        Primary: "#4FD1E8", PrimaryDarken: "#2FB4CC", PrimaryLighten: "#8EE6F3", PrimaryContrastText: "#04202A",
        Secondary: "#3DDC97", SecondaryLighten: "#8BEBC1", SecondaryContrastText: "#04202A", Tertiary: "#F2C14E",
        Info: "#4FD1E8", Success: "#3DDC97", Warning: "#F2C14E", Error: "#FF8A80",
        Background: "#0B1E26", Surface: "#12303A", AppBarBackground: "#0A2A33", AppBarText: "#E6F7FA",
        DrawerBackground: "#082228", DrawerText: "#E6F7FA", DrawerIcon: "#BDEFF6", DrawerSelected: "#16404B",
        TextPrimary: "#E6F7FA", TextSecondary: "#9CC3CC", Divider: "#1F4A55", TableLines: "#1A3F49", TableHover: "#16404B");

    /// <summary>Sand-inspired: warm cream surfaces, terracotta accent, sage secondary.</summary>
    public static SafiPalette Desert { get; } = new(
        Primary: "#9A5B1E", PrimaryDarken: "#7A4614", PrimaryLighten: "#B8732F", PrimaryContrastText: "#FFFFFF",
        Secondary: "#3F7D5C", SecondaryLighten: "#5C9C79", SecondaryContrastText: "#FFFFFF", Tertiary: "#A33A2A",
        Info: "#2F6E8C", Success: "#3F7D5C", Warning: "#B86E00", Error: "#A33A2A",
        Background: "#F6EFE3", Surface: "#FFFBF4", AppBarBackground: "#8A5A2B", AppBarText: "#FFF8EE",
        DrawerBackground: "#7A4E25", DrawerText: "#FFF3E2", DrawerIcon: "#F3DEC2", DrawerSelected: "#9A6634",
        TextPrimary: "#3B2A1A", TextSecondary: "#7A6650", Divider: "#E2D3BC", TableLines: "#EADFCC", TableHover: "#FBF4E8");

    /// <summary>Twilight: muted violet surfaces, lavender accent, rose secondary.</summary>
    public static SafiPalette Dusk { get; } = new(
        Primary: "#B39DDB", PrimaryDarken: "#9575CD", PrimaryLighten: "#D1C4E9", PrimaryContrastText: "#1A1625",
        Secondary: "#F4A6B8", SecondaryLighten: "#F8C9D4", SecondaryContrastText: "#1A1625", Tertiary: "#F6C177",
        Info: "#9CCFD8", Success: "#9CD8A8", Warning: "#F6C177", Error: "#EB6F92",
        Background: "#1A1625", Surface: "#252036", AppBarBackground: "#201A30", AppBarText: "#ECE7F5",
        DrawerBackground: "#18132A", DrawerText: "#ECE7F5", DrawerIcon: "#D1C4E9", DrawerSelected: "#2E2842",
        TextPrimary: "#ECE7F5", TextSecondary: "#ADA3C2", Divider: "#3A3350", TableLines: "#332C47", TableHover: "#2E2842");

    /// <summary>Night sky: near-black navy, starlight-blue accent, gold secondary.</summary>
    public static SafiPalette NightSky { get; } = new(
        Primary: "#7AA2FF", PrimaryDarken: "#5B86F0", PrimaryLighten: "#A9C1FF", PrimaryContrastText: "#070B1A",
        Secondary: "#FFD166", SecondaryLighten: "#FFE299", SecondaryContrastText: "#070B1A", Tertiary: "#C792EA",
        Info: "#7AA2FF", Success: "#7FD8A6", Warning: "#FFD166", Error: "#FF7A90",
        Background: "#070B1A", Surface: "#0F1630", AppBarBackground: "#0A1024", AppBarText: "#E4E9FF",
        DrawerBackground: "#060A18", DrawerText: "#E4E9FF", DrawerIcon: "#C3CFFF", DrawerSelected: "#162048",
        TextPrimary: "#E4E9FF", TextSecondary: "#9AA6CF", Divider: "#1E2A52", TableLines: "#1A2448", TableHover: "#162048");

    /// <summary>The palette for a theme name; System, Light and Dark use the SafiPay palettes.</summary>
    public static SafiPalette For(string? theme, bool isDark) => theme switch
    {
        "Aquatic" => Aquatic,
        "Desert" => Desert,
        "Dusk" => Dusk,
        "Night Sky" => NightSky,
        _ => isDark ? Dark : Light,
    };

    /// <summary>Resource key → colour, published as <c>Safi.{Name}Brush</c> and <c>Safi.{Name}Color</c>.</summary>
    public IEnumerable<(string Name, Color Color)> Entries()
    {
        yield return (nameof(Primary), Parse(Primary));
        yield return (nameof(PrimaryDarken), Parse(PrimaryDarken));
        yield return (nameof(PrimaryLighten), Parse(PrimaryLighten));
        yield return (nameof(PrimaryContrastText), Parse(PrimaryContrastText));
        yield return (nameof(Secondary), Parse(Secondary));
        yield return (nameof(SecondaryLighten), Parse(SecondaryLighten));
        yield return (nameof(SecondaryContrastText), Parse(SecondaryContrastText));
        yield return (nameof(Tertiary), Parse(Tertiary));
        yield return (nameof(Info), Parse(Info));
        yield return (nameof(Success), Parse(Success));
        yield return (nameof(Warning), Parse(Warning));
        yield return (nameof(Error), Parse(Error));
        yield return (nameof(Background), Parse(Background));
        yield return (nameof(Surface), Parse(Surface));
        yield return (nameof(AppBarBackground), Parse(AppBarBackground));
        yield return (nameof(AppBarText), Parse(AppBarText));
        yield return (nameof(DrawerBackground), Parse(DrawerBackground));
        yield return (nameof(DrawerText), Parse(DrawerText));
        yield return (nameof(DrawerIcon), Parse(DrawerIcon));
        yield return (nameof(DrawerSelected), Parse(DrawerSelected));
        yield return (nameof(TextPrimary), Parse(TextPrimary));
        yield return (nameof(TextSecondary), Parse(TextSecondary));
        yield return (nameof(Divider), Parse(Divider));
        yield return (nameof(TableLines), Parse(TableLines));
        yield return (nameof(TableHover), Parse(TableHover));
    }

    public static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
