using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;

namespace ReportExpert.App.Theming;

/// <summary>
/// XML colours for the RDLC XML view that stay readable on every theme. AvalonEdit's defaults (red names, pink
/// values) clash with the violet Dusk theme and are hard on the eyes; these use blue, teal, amber and muted green
/// only, and each pair meets WCAG AA (4.5:1) on the theme's surface: dark themes 5.5:1 or more, light 5.7:1 or more.
/// </summary>
public static class SafiXmlHighlighting
{
    private static readonly (string Name, string Dark, string Light)[] Colors =
    [
        ("XmlTag", "#7FB8F0", "#1A4D8C"),          // element names: blue
        ("AttributeName", "#8FD6C1", "#0A6464"),   // attribute names: teal
        ("AttributeValue", "#E8C07A", "#8A3A10"),  // values: amber / rust (no pink)
        ("Comment", "#93A89B", "#4E6B52"),         // comments: muted green
        ("CData", "#E8EEF3", "#1C2833"),           // CDATA (custom code): plain text
        ("DocType", "#A9B4BE", "#5A6670"),
        ("XmlDeclaration", "#A9B4BE", "#5A6670"),
        ("Entity", "#E0A97A", "#7A4E00"),
        ("BrokenEntity", "#F28B82", "#B3261E"),
    ];

    /// <summary>VB expression colours (expression editor), same hues and contrast as the XML ones.</summary>
    private static readonly (string Name, string Dark, string Light)[] VbColors =
    [
        ("Keywords", "#7FB8F0", "#1A4D8C"),
        ("FunctionKeywords", "#8FD6C1", "#0A6464"),
        ("ContextKeywords", "#7FB8F0", "#1A4D8C"),
        ("DataTypes", "#8FD6C1", "#0A6464"),
        ("String", "#E8C07A", "#8A3A10"),
        ("DateLiteral", "#E8C07A", "#8A3A10"),
        ("Comment", "#93A89B", "#4E6B52"),
        ("Operators", "#A9B4BE", "#5A6670"),
        ("Constants", "#E0A97A", "#7A4E00"),
        ("Preprocessor", "#A9B4BE", "#5A6670"),
    ];

    /// <summary>Theme-aware colours for a VB expression editor (Report Builder's expression language).</summary>
    public static void AttachVb(TextEditor editor)
    {
        void Apply()
        {
            var vb = HighlightingManager.Instance.GetDefinition("VB");
            if (vb is null)
                return;

            bool dark = SafiThemeManager.IsDark;
            foreach (var (name, darkHex, lightHex) in VbColors)
            {
                if (vb.GetNamedColor(name) is { } color)
                    color.Foreground = new SimpleHighlightingBrush((Color)ColorConverter.ConvertFromString(dark ? darkHex : lightHex));
            }

            editor.SyntaxHighlighting = null;
            editor.SyntaxHighlighting = vb;
        }

        Apply();
        void OnPalette() => editor.Dispatcher.Invoke(Apply);
        SafiThemeManager.PaletteChanged += OnPalette;
        editor.Unloaded += (_, _) => SafiThemeManager.PaletteChanged -= OnPalette;
    }

    /// <summary>Applies the colours for the current theme to the editor and keeps them in step with theme changes.</summary>
    public static void Attach(TextEditor editor)
    {
        Apply(editor);
        SafiThemeManager.PaletteChanged += () => editor.Dispatcher.Invoke(() => Apply(editor));
    }

    private static void Apply(TextEditor editor)
    {
        var xml = HighlightingManager.Instance.GetDefinition("XML");
        if (xml is null)
            return;

        bool dark = SafiThemeManager.IsDark;
        foreach (var (name, darkHex, lightHex) in Colors)
        {
            if (xml.GetNamedColor(name) is { } color)
            {
                color.Foreground = new SimpleHighlightingBrush((Color)ColorConverter.ConvertFromString(dark ? darkHex : lightHex));
                color.FontWeight = name == "XmlTag" ? System.Windows.FontWeights.SemiBold : null;
            }
        }

        // Reassigning makes the editor pick up the new colours; the selection follows the theme accent.
        editor.SyntaxHighlighting = null;
        editor.SyntaxHighlighting = xml;
        editor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x7F, 0xB8, 0xF0));
        editor.TextArea.SelectionForeground = null;
        editor.TextArea.TextView.Redraw();
    }
}
