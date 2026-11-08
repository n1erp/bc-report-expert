using System.Xml.Linq;

namespace ReportExpert.Reporting.Design;

/// <summary>
/// The formatting the designer draws for a text box or cell: font, emphasis, colours, alignment and border,
/// read from the same places the format toolbar and the Properties pane write them (the first text run, the
/// first paragraph and the item's own style). Values that are expressions (starting with <c>=</c>) are
/// reported as empty, because they only have a value at run time.
/// </summary>
public sealed record DesignTextStyle(
    string FontFamily,
    string FontSize,
    bool Bold,
    bool Italic,
    bool Underline,
    bool Strikeout,
    string Color,
    string BackgroundColor,
    string TextAlign,
    string VerticalAlign,
    string BorderStyle,
    string BorderColor,
    string BorderWidth)
{
    /// <summary>No formatting: the designer's defaults apply.</summary>
    public static DesignTextStyle Empty { get; } = new("", "", false, false, false, false, "", "", "", "", "", "", "");

    /// <summary>Reads the formatting of a report item (a text box, rectangle, or the contents of a cell).</summary>
    public static DesignTextStyle Read(XElement? item)
    {
        if (item is null)
        {
            return Empty;
        }

        var ns = item.Name.Namespace;
        var own = item.Element(ns + "Style");

        // Only the item's own paragraphs: a rectangle's nested text boxes have their own style.
        var paragraph = item.Element(ns + "Paragraphs")?.Element(ns + "Paragraph");
        var run = paragraph?.Element(ns + "TextRuns")?.Element(ns + "TextRun")?.Element(ns + "Style");
        var paragraphStyle = paragraph?.Element(ns + "Style");
        var border = own?.Element(ns + "Border");

        string Value(XElement? style, string name) =>
            style?.Element(ns + name)?.Value is { } text && !text.TrimStart().StartsWith('=') ? text.Trim() : "";

        var weight = Value(run, "FontWeight");
        var decoration = Value(run, "TextDecoration");
        return new DesignTextStyle(
            Value(run, "FontFamily"),
            Value(run, "FontSize"),
            weight is "Bold" or "ExtraBold" or "Heavy" or "SemiBold" or "Medium",
            Value(run, "FontStyle").Equals("Italic", StringComparison.OrdinalIgnoreCase),
            decoration.Equals("Underline", StringComparison.OrdinalIgnoreCase),
            decoration.Equals("LineThrough", StringComparison.OrdinalIgnoreCase),
            Value(run, "Color"),
            Value(own, "BackgroundColor"),
            Value(paragraphStyle, "TextAlign"),
            Value(own, "VerticalAlign"),
            Value(border, "Style"),
            Value(border, "Color"),
            Value(border, "Width"));
    }
}
