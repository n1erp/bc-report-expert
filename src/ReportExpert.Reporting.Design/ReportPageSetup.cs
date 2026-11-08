using System.Globalization;
using System.Text.RegularExpressions;
using ReportExpert.Rdl.Core;

namespace ReportExpert.Reporting.Design;

/// <summary>
/// The page a report prints on: paper, orientation, margins and columns, plus the unit the layout was authored in
/// (<c>rd:ReportUnitType</c>, "Cm" or "Inch"), which the rulers and the page setup dialog use.
/// </summary>
public sealed record DesignPageSetup(
    string PaperSize,
    string Orientation,
    double PageWidthInches,
    double PageHeightInches,
    double LeftMarginInches,
    double RightMarginInches,
    double TopMarginInches,
    double BottomMarginInches,
    int Columns,
    double ColumnSpacingInches,
    string Unit)
{
    /// <summary>The width between the left and right margins.</summary>
    public double PrintableWidthInches => Math.Max(0, PageWidthInches - LeftMarginInches - RightMarginInches);

    /// <summary>
    /// The widest the body may be without spilling onto extra pages: the printable width, shared between
    /// columns when the page has several.
    /// </summary>
    public double MaxBodyWidthInches => Columns > 1
        ? Math.Max(0, (PrintableWidthInches - ColumnSpacingInches * (Columns - 1)) / Columns)
        : PrintableWidthInches;

    public bool IsMetric => !string.Equals(Unit, "Inch", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One property change in a batch: the item (or <see cref="ReportPageSetup.ReportTarget"/>), the key and the value.</summary>
public sealed record ReportPropertyChange(string Target, string Key, string? Value);

public static class ReportPageSetup
{
    /// <summary>The property target that means the report itself (page setup, body width).</summary>
    public const string ReportTarget = ReportPropertySheet.ReportTarget;

    /// <summary>Standard paper sizes, portrait, in inches (short side first).</summary>
    public static IReadOnlyList<(string Name, double Width, double Height)> PaperSizes { get; } =
    [
        ("Letter", 8.5, 11),
        ("Legal", 8.5, 14),
        ("A3", 11.69, 16.54),
        ("A4", 8.27, 11.69),
        ("A5", 5.83, 8.27),
        ("B5", 6.93, 9.84),
        ("Executive", 7.25, 10.5),
        ("Tabloid", 11, 17),
    ];

    /// <summary>Tolerance for the blank-page check, so rounding in cm↔in conversions does not raise it.</summary>
    private const double Tolerance = 0.005;

    public static DesignPageSetup Read(RdlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var ns = document.Ns;
        var page = document.Descendants("Page").FirstOrDefault();
        double Length(string name, double fallback)
        {
            var value = page?.Element(ns + name)?.Value;
            return string.IsNullOrWhiteSpace(value) ? fallback : Dimension.ToInches(value);
        }

        var width = Length("PageWidth", 8.5);
        var height = Length("PageHeight", 11);
        var columns = int.TryParse(page?.Element(ns + "Columns")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? Math.Max(1, count) : 1;
        var unit = document.Root.Element(RdlDocument.Rd + "ReportUnitType")?.Value;
        var sheet = ReportPropertySheet.Read(document, ReportPropertySheet.ReportTarget)
            .SelectMany(section => section.Properties)
            .ToDictionary(property => property.Key, property => property.Value, StringComparer.Ordinal);
        return new DesignPageSetup(
            sheet.TryGetValue("PaperSize", out var paper) ? paper : "Custom",
            width > height ? "Landscape" : "Portrait",
            width,
            height,
            Length("LeftMargin", 0),
            Length("RightMargin", 0),
            Length("TopMargin", 0),
            Length("BottomMargin", 0),
            columns,
            Length("ColumnSpacing", 0.5),
            string.Equals(unit, "Inch", StringComparison.OrdinalIgnoreCase) ? "Inch" : "Cm");
    }

    /// <summary>
    /// Microsoft's rule for paginated reports: body width ≤ page width − (left margin + right margin). A wider
    /// body prints the overflow on extra, mostly blank, pages.
    /// </summary>
    public static string? BlankPageWarning(DesignPageSetup page, double bodyWidthInches)
    {
        if (bodyWidthInches <= page.MaxBodyWidthInches + Tolerance)
        {
            return null;
        }

        string Show(double inches) => Format(inches, page.IsMetric);
        return $"The body is {Show(bodyWidthInches)} wide but only {Show(page.MaxBodyWidthInches)} fits between the margins "
            + $"(page {Show(page.PageWidthInches)} − margins {Show(page.LeftMarginInches)} + {Show(page.RightMarginInches)}"
            + (page.Columns > 1 ? $", {page.Columns} columns" : "")
            + "). It will print extra blank pages: narrow the body (Page setup › Fit body to page) or the margins.";
    }

    /// <summary>A length in the report's unit, such as "19.4cm" or "7.64in".</summary>
    public static string Format(double inches, bool metric) => metric
        ? (inches * 2.54).ToString("0.##", CultureInfo.InvariantCulture) + "cm"
        : inches.ToString("0.###", CultureInfo.InvariantCulture) + "in";
}

/// <summary>
/// How the design surface shows a text box value, the way Report Builder does: a simple reference in brackets
/// (<c>[Amount]</c>, <c>[Sum(Amount)]</c>, <c>[@Year]</c>, <c>[&amp;PageNumber]</c>), anything more complex as
/// <c>«Expr»</c>, and literal text as is. The full expression stays in the tooltip and the editors.
/// </summary>
public static partial class ReportExpressionDisplay
{
    public const string Complex = "«Expr»";

    public static string Placeholder(string? value)
    {
        var text = (value ?? "").Trim();
        if (!text.StartsWith('='))
        {
            return value ?? "";
        }

        var body = text[1..].Trim();
        if (FieldRef().Match(body) is { Success: true } field)
        {
            return $"[{field.Groups["name"].Value}]";
        }

        if (AggregateRef().Match(body) is { Success: true } aggregate)
        {
            return $"[{aggregate.Groups["fn"].Value}({aggregate.Groups["name"].Value})]";
        }

        if (ParameterRef().Match(body) is { Success: true } parameter)
        {
            return $"[@{parameter.Groups["name"].Value}{(parameter.Groups["part"].Value == "Label" ? ".Label" : "")}]";
        }

        if (GlobalRef().Match(body) is { Success: true } global)
        {
            return $"[&{global.Groups["name"].Value}]";
        }

        if (StringLiteral().Match(body) is { Success: true } literal)
        {
            // ="Invoice" is just a caption written as an expression.
            return literal.Groups["text"].Value.Replace("\"\"", "\"", StringComparison.Ordinal);
        }

        return Complex;
    }

    [GeneratedRegex(@"^Fields!(?<name>\w+)\.Value$", RegexOptions.IgnoreCase)]
    private static partial Regex FieldRef();

    [GeneratedRegex(@"^(?<fn>Sum|Avg|Min|Max|Count|CountDistinct|First|Last)\(\s*Fields!(?<name>\w+)\.Value\s*(,\s*""[^""]*""\s*)?\)$", RegexOptions.IgnoreCase)]
    private static partial Regex AggregateRef();

    [GeneratedRegex(@"^Parameters!(?<name>\w+)\.(?<part>Value|Label)$", RegexOptions.IgnoreCase)]
    private static partial Regex ParameterRef();

    [GeneratedRegex(@"^Globals!(?<name>\w+)(\.Value)?$", RegexOptions.IgnoreCase)]
    private static partial Regex GlobalRef();

    [GeneratedRegex(@"^""(?<text>([^""]|"""")*)""$")]
    private static partial Regex StringLiteral();
}
