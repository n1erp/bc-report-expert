using System.Globalization;
using System.Xml.Linq;
using ReportExpert.Rdl.Core;

namespace ReportExpert.Reporting.Design;

/// <summary>
/// The properties panel model. For a target (the report, a page band, a report item or a table cell) it
/// lists collapsible sections of typed properties, and it sets or resets one property at a time.
/// A value that starts with <c>=</c> is an expression; <see cref="ReportDefinitionGuard"/> checks it on save.
/// </summary>
internal static class ReportPropertySheet
{
    public const string ReportTarget = "@Report";

    public const string HeaderTarget = "@PageHeader";

    public const string FooterTarget = "@PageFooter";

    public static readonly string[] Fonts = ["Arial", "Calibri", "Cambria", "Courier New", "Georgia", "Segoe UI", "Tahoma", "Times New Roman", "Verdana"];

    public static readonly string[] Formats = ["N0", "N2", "#,0.00", "C2", "P0", "P2", "d", "D", "dd/MM/yyyy", "MM/yyyy", "yyyy-MM-dd", "HH:mm"];

    private static readonly string[] Weights = ["Normal", "Bold", "Light", "SemiBold", "ExtraBold"];

    private static readonly string[] FontStyles = ["Normal", "Italic"];

    private static readonly string[] Decorations = ["None", "Underline", "Overline", "LineThrough"];

    private static readonly string[] TextAligns = ["General", "Left", "Center", "Right"];

    private static readonly string[] VerticalAligns = ["Top", "Middle", "Bottom"];

    private static readonly string[] BorderStyles = ["None", "Solid", "Dashed", "Dotted", "Double"];

    private static readonly string[] BorderWidths = ["0.5pt", "1pt", "1.5pt", "2pt", "3pt"];

    private static readonly string[] Sizings = ["AutoSize", "Fit", "FitProportional", "Clip"];

    private static readonly string[] BreakLocations = ["None", "Start", "End", "StartAndEnd", "Between"];

    private static readonly string[] Directions = ["Ascending", "Descending"];

    private static readonly string[] Orientations = ["Portrait", "Landscape"];

    private static IReadOnlyList<(string Name, double Width, double Height)> PaperSizes => ReportPageSetup.PaperSizes;

    private delegate string Getter(Context context);

    private delegate void Setter(Context context, string? value);

    private delegate IReadOnlyList<string> OptionSource(Context context);

    public static IReadOnlyList<DesignPropertySection> Read(RdlDocument document, string target)
    {
        var context = Resolve(document, target);
        return Definitions(context.Kind)
            .Where(definition => definition.When?.Invoke(context) ?? true)
            .GroupBy(definition => definition.Section)
            .Select(group => new DesignPropertySection(
                group.Key,
                group.Select(definition => new DesignProperty(
                        definition.Key,
                        definition.Editor,
                        definition.Get(context),
                        definition.Options?.Invoke(context),
                        definition.Expression))
                    .ToArray()))
            .ToArray();
    }

    public static void Set(RdlDocument document, string target, string key, string? value)
    {
        var context = Resolve(document, target);
        var definition = Definitions(context.Kind).FirstOrDefault(candidate => candidate.Key == key && (candidate.When?.Invoke(context) ?? true))
            ?? throw new ReportDesignException(ReportCodes.InvalidItem);
        var text = value?.Trim() ?? "";
        if (text.Length == 0)
        {
            definition.Set(context, null);
            return;
        }

        if (text.StartsWith('='))
        {
            if (!definition.Expression || text.Length > 2000)
            {
                throw new ReportDesignException(ReportCodes.InvalidValue);
            }

            definition.Set(context, text);
            return;
        }

        definition.Set(context, Validate(definition, context, text));
    }

    public static void Reset(RdlDocument document, string target, string key) => Set(document, target, key, null);

    /// <summary>The kind the panel shows for a target, or null when the target does not exist.</summary>
    public static string? KindOf(RdlDocument document, string target)
    {
        try
        {
            return Resolve(document, target).Kind;
        }
        catch (ReportDesignException)
        {
            return null;
        }
    }

    private static string Validate(Definition definition, Context context, string text)
    {
        switch (definition.Editor)
        {
            case "length":
                return Dimension.FromInches(Math.Clamp(Length(text), 0, 60));
            case "points":
                var points = ParseNumber(text.Replace("pt", "", StringComparison.OrdinalIgnoreCase));
                return Math.Clamp(points, 1, 200).ToString("0.#", CultureInfo.InvariantCulture) + "pt";
            case "padding":
                var padding = ParseNumber(text.Replace("pt", "", StringComparison.OrdinalIgnoreCase));
                return Math.Clamp(padding, 0, 200).ToString("0.#", CultureInfo.InvariantCulture) + "pt";
            case "number":
                return ParseNumber(text).ToString("0.######", CultureInfo.InvariantCulture);
            case "integer":
                return ((int)Math.Clamp(ParseNumber(text), 1, 10)).ToString(CultureInfo.InvariantCulture);
            case "check":
                return text.ToLowerInvariant() switch
                {
                    "true" or "on" or "1" => "true",
                    "false" or "off" or "0" => "false",
                    _ => throw new ReportDesignException(ReportCodes.InvalidValue),
                };
            case "color":
                if (text.StartsWith('#') ? text.Length is 4 or 7 or 9 && text[1..].All(char.IsAsciiHexDigit) : text.All(char.IsAsciiLetter) && text.Length <= 30)
                {
                    return text;
                }

                throw new ReportDesignException(ReportCodes.InvalidValue);
            case "select":
                var options = definition.Options?.Invoke(context) ?? [];
                return options.Contains(text, StringComparer.Ordinal) ? text : throw new ReportDesignException(ReportCodes.InvalidValue);
            default:
                return text.Length > 500 ? text[..500] : text;
        }
    }

    private static double Length(string text)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var inches))
        {
            return inches;
        }

        return Dimension.TryToInches(text.Replace(',', '.'), out var parsed) ? parsed : throw new ReportDesignException(ReportCodes.InvalidValue);
    }

    private static double ParseNumber(string text) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new ReportDesignException(ReportCodes.InvalidValue);

    private static Context Resolve(RdlDocument document, string target)
    {
        var ns = document.Ns;
        var page = document.Descendants("Page").FirstOrDefault() ?? throw new ReportDesignException(ReportCodes.InvalidDefinition);
        switch (target)
        {
            case ReportTarget:
                return new Context(document, document.Root, ns, "Report", page);
            case HeaderTarget:
                return new Context(document, page.Element(ns + "PageHeader") ?? throw new ReportDesignException(ReportCodes.InvalidItem), ns, "PageHeader", page);
            case FooterTarget:
                return new Context(document, page.Element(ns + "PageFooter") ?? throw new ReportDesignException(ReportCodes.InvalidItem), ns, "PageFooter", page);
        }

        if (!ReportDefinitionGuard.ValidIdentifier(target))
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        var element = document.Root.Descendants()
            .FirstOrDefault(candidate => string.Equals(candidate.Attribute("Name")?.Value, target, StringComparison.Ordinal)
                && candidate.Name.LocalName is "Textbox" or "Rectangle" or "Line" or "Image" or "Tablix" or "Chart" or "Gauge" or "Map")
            ?? throw new ReportDesignException(ReportCodes.InvalidItem);
        var kind = element.Name.LocalName;
        if (kind == "Textbox" && element.Parent?.Name.LocalName == "CellContents")
        {
            kind = "Cell";
        }

        return new Context(document, element, ns, kind, page);
    }

    private static IEnumerable<Definition> Definitions(string kind) => kind switch
    {
        "Report" => ReportDefinitions,
        "PageHeader" or "PageFooter" => BandDefinitions,
        "Textbox" => [.. TextDefinitions, .. BoxDefinitions, .. PositionDefinitions, .. VisibilityDefinitions, .. TextBoxExtraDefinitions, .. InteractivityDefinitions],
        "Cell" => [.. TextDefinitions, .. BoxDefinitions, .. VisibilityDefinitions, .. TextBoxExtraDefinitions, .. InteractivityDefinitions],
        "Rectangle" => [.. BoxDefinitions, .. PositionDefinitions, .. VisibilityDefinitions, .. RectangleExtraDefinitions, .. InteractivityDefinitions.Take(2)],
        "Line" => [.. LineDefinitions, .. PositionDefinitions, .. VisibilityDefinitions],
        "Image" => [.. ImageDefinitions, .. BoxDefinitions, .. PositionDefinitions, .. VisibilityDefinitions, .. InteractivityDefinitions],
        "Tablix" => [.. TablixDefinitions, .. TableTextDefinitions, .. BoxDefinitions, .. PositionDefinitions, .. VisibilityDefinitions],
        "Chart" => [.. ChartDefinitions, .. AxisDefinitions(category: true), .. AxisDefinitions(category: false), .. BoxDefinitions, .. PositionDefinitions, .. VisibilityDefinitions],
        _ => [.. BoxDefinitions, .. PositionDefinitions, .. VisibilityDefinitions],
    };

    private static readonly Definition[] TextDefinitions =
    [
        new("Value", "Basic", "textarea", context => RunValue(context.Element, context.Ns), (context, value) => SetRunValue(context, value ?? "")),
        new("FontFamily", "Font", "select", context => Get(RunStyle(context), context.Ns, "FontFamily", "Arial"), (context, value) => Put(RunStyle(context), context.Ns, "FontFamily", value), _ => Fonts),
        new("FontSize", "Font", "points", context => Get(RunStyle(context), context.Ns, "FontSize", "10pt"), (context, value) => Put(RunStyle(context), context.Ns, "FontSize", value)),
        new("FontWeight", "Font", "select", context => Get(RunStyle(context), context.Ns, "FontWeight", "Normal"), (context, value) => Put(RunStyle(context), context.Ns, "FontWeight", value), _ => Weights),
        new("FontStyle", "Font", "select", context => Get(RunStyle(context), context.Ns, "FontStyle", "Normal"), (context, value) => Put(RunStyle(context), context.Ns, "FontStyle", value), _ => FontStyles),
        new("TextDecoration", "Font", "select", context => Get(RunStyle(context), context.Ns, "TextDecoration", "None"), (context, value) => Put(RunStyle(context), context.Ns, "TextDecoration", value), _ => Decorations),
        new("Color", "Font", "color", context => Get(RunStyle(context), context.Ns, "Color", "Black"), (context, value) => Put(RunStyle(context), context.Ns, "Color", value)),
        new("Format", "Font", "format", context => Get(RunStyle(context), context.Ns, "Format", ""), (context, value) => Put(RunStyle(context), context.Ns, "Format", value), _ => Formats),
        new("TextAlign", "Alignment", "select", context => Get(ParagraphStyle(context), context.Ns, "TextAlign", "General"), (context, value) => Put(ParagraphStyle(context), context.Ns, "TextAlign", value), _ => TextAligns),
        new("VerticalAlign", "Alignment", "select", context => Get(ItemStyle(context), context.Ns, "VerticalAlign", "Top"), (context, value) => Put(ItemStyle(context), context.Ns, "VerticalAlign", value), _ => VerticalAligns),
        new("CanGrow", "Alignment", "check", context => Get(context.Element, context.Ns, "CanGrow", "false"), (context, value) => Put(context.Element, context.Ns, "CanGrow", value), Expression: false),
        new("CanShrink", "Alignment", "check", context => Get(context.Element, context.Ns, "CanShrink", "false"), (context, value) => Put(context.Element, context.Ns, "CanShrink", value), Expression: false),
    ];

    private static readonly Definition[] TableTextDefinitions =
    [
        new("FontFamily", "Font", "select", context => Get(FirstCellRun(context), context.Ns, "FontFamily", "Arial"), (context, value) => PutAllCells(context, "FontFamily", value), _ => Fonts),
        new("FontSize", "Font", "points", context => Get(FirstCellRun(context), context.Ns, "FontSize", "10pt"), (context, value) => PutAllCells(context, "FontSize", value)),
        new("Color", "Font", "color", context => Get(FirstCellRun(context), context.Ns, "Color", "Black"), (context, value) => PutAllCells(context, "Color", value)),
    ];

    private static readonly Definition[] BoxDefinitions =
    [
        new("BackgroundColor", "Appearance", "color", context => Get(ItemStyle(context), context.Ns, "BackgroundColor", ""), (context, value) => Put(ItemStyle(context), context.Ns, "BackgroundColor", value)),
        new("BorderStyle", "Appearance", "select", context => Get(Border(context), context.Ns, "Style", "None"), (context, value) => Put(Border(context), context.Ns, "Style", value), _ => BorderStyles),
        new("BorderColor", "Appearance", "color", context => Get(Border(context), context.Ns, "Color", "Black"), (context, value) => Put(Border(context), context.Ns, "Color", value)),
        new("BorderWidth", "Appearance", "select", context => Get(Border(context), context.Ns, "Width", "1pt"), (context, value) => Put(Border(context), context.Ns, "Width", value), _ => BorderWidths),
    ];

    private static readonly Definition[] LineDefinitions =
    [
        new("BorderColor", "Appearance", "color", context => Get(Border(context), context.Ns, "Color", "Black"), (context, value) => Put(Border(context), context.Ns, "Color", value)),
        new("BorderStyle", "Appearance", "select", context => Get(Border(context), context.Ns, "Style", "Solid"), (context, value) => Put(Border(context), context.Ns, "Style", value), _ => BorderStyles),
        new("BorderWidth", "Appearance", "select", context => Get(Border(context), context.Ns, "Width", "1pt"), (context, value) => Put(Border(context), context.Ns, "Width", value), _ => BorderWidths),
    ];

    private static readonly Definition[] ImageDefinitions =
    [
        new("ImageValue", "Basic", "select", context => Get(context.Element, context.Ns, "Value", ""), (context, value) => PutRequired(context.Element, context.Ns, "Value", value), context => ReportImageEditor.Names(context.Document), Expression: false),
        new("Sizing", "Basic", "select", context => Get(context.Element, context.Ns, "Sizing", "AutoSize"), (context, value) => Put(context.Element, context.Ns, "Sizing", value), _ => Sizings, Expression: false),
    ];

    private static readonly Definition[] TablixDefinitions =
    [
        new("DataSetName", "Data", "select", context => Get(context.Element, context.Ns, "DataSetName", ""), (context, value) => ReportDataSetEditor.Bind(context.Document, Name(context), value ?? throw new ReportDesignException(ReportCodes.InvalidValue)), context => ReportDataSetEditor.Names(context.Document), Expression: false),
        new("SortField", "Data", "select", context => TableSort(context).Field, (context, value) => SetTableSort(context, value, null), context => [.. FieldNames(context)], Expression: false),
        new("SortDirection", "Data", "select", context => TableSort(context).Direction, (context, value) => SetTableSort(context, null, value ?? "Ascending"), _ => Directions, Expression: false),
        new("NoRowsMessage", "Data", "text", context => Get(context.Element, context.Ns, "NoRowsMessage", ""), (context, value) => Put(context.Element, context.Ns, "NoRowsMessage", value)),
        new("RepeatHeader", "PageBreak", "check", context => RepeatsHeader(context) ? "true" : "false", (context, value) => ReportTableEditor.RepeatHeader(context.Document, Name(context), value == "true"), Expression: false),
        new("BreakLocation", "PageBreak", "select", context => Get(context.Element.Element(context.Ns + "PageBreak"), context.Ns, "BreakLocation", "None"), (context, value) => Put(Child(context.Element, context.Ns, "PageBreak"), context.Ns, "BreakLocation", value), _ => BreakLocations, Expression: false),
        new("KeepTogether", "PageBreak", "check", context => Get(context.Element, context.Ns, "KeepTogether", "false"), (context, value) => Put(context.Element, context.Ns, "KeepTogether", value), Expression: false),
    ];

    private static readonly Definition[] ChartDefinitions =
    [
        new("DataSetName", "Data", "select", context => Get(context.Element, context.Ns, "DataSetName", ""), (context, value) => SetChartDataSet(context, value), context => ReportDataSetEditor.Names(context.Document), Expression: false),
        new("ChartType", "Chart", "select", context => Chart(context).ChartType, (context, value) => EditChart(context, chart => chart with { ChartType = value ?? "Column" }), _ => ReportChartEditor.ChartTypes, Expression: false),
        new("ChartTitle", "Chart", "text", context => Chart(context).Title, (context, value) => EditChart(context, chart => chart with { Title = value ?? "" }), Expression: false),
        new("Category", "Chart", "select", context => Chart(context).Category, (context, value) => EditChart(context, chart => chart with { Category = value ?? chart.Category }), context => [.. FieldNames(context)], Expression: false),
        new("Aggregate", "Chart", "select", context => Chart(context).Aggregate, (context, value) => EditChart(context, chart => chart with { Aggregate = value ?? "Count" }), _ => ReportChartEditor.Aggregates, Expression: false),
        new("ValueField", "Chart", "select", context => Chart(context).ValueField, (context, value) => EditChart(context, chart => chart with { ValueField = value ?? chart.ValueField }), context => [.. FieldNames(context)], Expression: false),
        new("Series", "Chart", "select", context => Chart(context).Series, (context, value) => EditChart(context, chart => chart with { Series = value ?? "" }), context => ["", .. FieldNames(context)], Expression: false),
        new("ShowLegend", "Chart", "check", context => Chart(context).ShowLegend ? "true" : "false", (context, value) => EditChart(context, chart => chart with { ShowLegend = value == "true" }), Expression: false),
        new("ShowLabels", "Chart", "check", context => Chart(context).ShowLabels ? "true" : "false", (context, value) => EditChart(context, chart => chart with { ShowLabels = value == "true" }), Expression: false),
        new("SecondField", "Chart", "select", context => Chart(context).SecondField, (context, value) => EditChart(context, chart => chart with { SecondField = value ?? "" }), context => [.. FieldNames(context)], Expression: false, When: context => ReportChartEditor.NeedsSecond(Chart(context).ChartType)),
        new("Palette", "Chart", "select", context => ReportChartEditor.Palette(context.Element, context.Ns), (context, value) => EditChart(context, chart => chart, value ?? "Pacific"), _ => ReportChartEditor.Palettes, Expression: false),
        new("LegendPosition", "Legend", "select", context => Get(Legend(context), context.Ns, "Position", "BottomCenter"), (context, value) => PutRequired(Legend(context) ?? throw new ReportDesignException(ReportCodes.InvalidItem), context.Ns, "Position", value ?? "BottomCenter"), _ => ReportChartEditor.LegendPositions, Expression: false, When: context => Legend(context) is not null),
        new("NoDataMessage", "NoData", "text", context => Get(context.Element.Element(context.Ns + "ChartNoDataMessage"), context.Ns, "Caption", ""), (context, value) => SetNoData(context, value)),
    ];

    /// <summary>
    /// Category and value axis settings of a chart that has axes (not pie, doughnut, funnel or pyramid).
    /// Minimum, maximum and interval are numbers; empty means automatic.
    /// </summary>
    private static Definition[] AxisDefinitions(bool category)
    {
        var section = category ? "CategoryAxis" : "ValueAxis";
        var prefix = category ? "CategoryAxis" : "ValueAxis";
        bool HasAxis(Context context) => !ReportChartEditor.IsShape(Chart(context).ChartType) && ReportChartEditor.AxisOf(context.Element, context.Ns, category) is not null;
        XElement Axis(Context context) => ReportChartEditor.AxisOf(context.Element, context.Ns, category) ?? throw new ReportDesignException(ReportCodes.InvalidItem);
        XElement AxisTitle(Context context) => Child(Axis(context), context.Ns, "ChartAxisTitle");
        XElement AxisStyle(Context context) => Child(Axis(context), context.Ns, "Style");
        XElement Grid(Context context) => Child(Axis(context), context.Ns, "ChartMajorGridLines");
        var list = new List<Definition>
        {
            new(prefix + "Title", section, "text", context => Get(Axis(context).Element(context.Ns + "ChartAxisTitle"), context.Ns, "Caption", ""), (context, value) => Put(AxisTitle(context), context.Ns, "Caption", value ?? ""), When: HasAxis),
            new(prefix + "Visible", section, "check", context => string.Equals(Get(Axis(context), context.Ns, "Visible", "True"), "False", StringComparison.OrdinalIgnoreCase) ? "false" : "true", (context, value) => Put(Axis(context), context.Ns, "Visible", value == "false" ? "False" : null), Expression: false, When: HasAxis),
            new(prefix + "Format", section, "format", context => Get(Axis(context).Element(context.Ns + "Style"), context.Ns, "Format", ""), (context, value) => Put(AxisStyle(context), context.Ns, "Format", value), _ => Formats, When: HasAxis),
            new(prefix + "Angle", section, "select", context => Get(Axis(context), context.Ns, "Angle", "Auto"), (context, value) => Put(Axis(context), context.Ns, "Angle", value is null or "Auto" ? null : value), _ => ReportChartEditor.Angles, Expression: false, When: HasAxis),
            new(prefix + "GridLines", section, "check", context => string.Equals(Get(Axis(context).Element(context.Ns + "ChartMajorGridLines"), context.Ns, "Enabled", "False"), "True", StringComparison.OrdinalIgnoreCase) ? "true" : "false", (context, value) => Put(Grid(context), context.Ns, "Enabled", value == "true" ? "True" : "False"), Expression: false, When: HasAxis),
            new(prefix + "Reverse", section, "check", context => Get(Axis(context), context.Ns, "Reverse", "false").ToLowerInvariant(), (context, value) => Put(Axis(context), context.Ns, "Reverse", value == "true" ? "true" : null), Expression: false, When: HasAxis),
            new(prefix + "Minimum", section, "number", context => AxisNumber(Axis(context), context.Ns, "Minimum"), (context, value) => SetAxisNumber(Axis(context), context.Ns, "Minimum", value), When: HasAxis),
            new(prefix + "Maximum", section, "number", context => AxisNumber(Axis(context), context.Ns, "Maximum"), (context, value) => SetAxisNumber(Axis(context), context.Ns, "Maximum", value), When: HasAxis),
            new(prefix + "Interval", section, "number", context => AxisNumber(Axis(context), context.Ns, "Interval"), (context, value) => SetAxisNumber(Axis(context), context.Ns, "Interval", value), When: HasAxis),
        };
        if (!category)
        {
            list.Add(new(prefix + "LogScale", section, "check", context => Get(Axis(context), context.Ns, "LogScale", "false").ToLowerInvariant(), (context, value) => SetLogScale(Axis(context), context.Ns, value == "true"), Expression: false, When: HasAxis));
        }

        return [.. list];
    }

    /// <summary>
    /// Sets a numeric axis bound. Literal values are checked against each other: the minimum stays below the maximum,
    /// the interval is positive, and a logarithmic axis starts above zero. Expressions are checked when the report renders.
    /// </summary>
    private static void SetAxisNumber(XElement axis, XNamespace ns, string name, string? value)
    {
        var minimum = name == "Minimum" ? value : AxisNumber(axis, ns, "Minimum");
        var maximum = name == "Maximum" ? value : AxisNumber(axis, ns, "Maximum");
        if (Literal(minimum) is double low && Literal(maximum) is double high && low >= high)
        {
            throw new ReportDesignException(ReportCodes.InvalidValue);
        }

        if (name == "Interval" && Literal(value) is <= 0)
        {
            throw new ReportDesignException(ReportCodes.InvalidValue);
        }

        if (name == "Minimum" && Literal(value) is <= 0 && string.Equals(Get(axis, ns, "LogScale", "false"), "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportDesignException(ReportCodes.InvalidValue);
        }

        Put(axis, ns, name, value);
    }

    private static void SetLogScale(XElement axis, XNamespace ns, bool on)
    {
        if (on && Literal(AxisNumber(axis, ns, "Minimum")) is <= 0)
        {
            throw new ReportDesignException(ReportCodes.InvalidValue);
        }

        Put(axis, ns, "LogScale", on ? "true" : null);
    }

    private static double? Literal(string? value) =>
        value is { Length: > 0 } && !value.StartsWith('=') && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    private static string AxisNumber(XElement axis, XNamespace ns, string name)
    {
        var value = Get(axis, ns, name, "");
        return value.Equals("NaN", StringComparison.OrdinalIgnoreCase) ? "" : value;
    }

    private static XElement? Legend(Context context) =>
        context.Element.Element(context.Ns + "ChartLegends")?.Elements(context.Ns + "ChartLegend").FirstOrDefault();

    private static void SetNoData(Context context, string? value)
    {
        var message = context.Element.Element(context.Ns + "ChartNoDataMessage");
        if (message is null)
        {
            message = new XElement(context.Ns + "ChartNoDataMessage", new XAttribute("Name", "NoDataMessage"), new XElement(context.Ns + "Style", new XElement(context.Ns + "FontSize", "9pt")));
            context.Element.Element(context.Ns + "DataSetName")?.AddBeforeSelf(message);
            if (message.Parent is null)
            {
                context.Element.Add(message);
            }
        }

        message.SetElementValue(context.Ns + "Caption", value ?? "");
    }

    private static readonly Definition[] PositionDefinitions =
    [
        new("Left", "Position", "length", context => Get(context.Element, context.Ns, "Left", "0in"), (context, value) => Move(context, left: value), Expression: false),
        new("Top", "Position", "length", context => Get(context.Element, context.Ns, "Top", "0in"), (context, value) => Move(context, top: value), Expression: false),
        new("Width", "Position", "length", context => Dimension.FromInches(ItemWidth(context)), (context, value) => Move(context, width: value), Expression: false),
        new("Height", "Position", "length", context => Dimension.FromInches(ItemHeight(context)), (context, value) => Move(context, height: value), Expression: false),
    ];

    private static readonly Definition[] VisibilityDefinitions =
    [
        new("Hidden", "Visibility", "check", context => Get(context.Element.Element(context.Ns + "Visibility"), context.Ns, "Hidden", "false"), (context, value) => SetHidden(context, value)),
        new("ToolTip", "Visibility", "text", context => Get(context.Element, context.Ns, "ToolTip", ""), (context, value) => Put(context.Element, context.Ns, "ToolTip", value)),
        new("ToggleItem", "Visibility", "select", context => Get(context.Element.Element(context.Ns + "Visibility"), context.Ns, "ToggleItem", ""), SetToggleItem, ToggleCandidates, Expression: false),
    ];

    /// <summary>Bookmarks, document map and actions (go to URL / go to bookmark), as the Action page offers.</summary>
    private static readonly Definition[] InteractivityDefinitions =
    [
        new("Bookmark", "Interactivity", "text", context => Get(context.Element, context.Ns, "Bookmark", ""), (context, value) => Put(context.Element, context.Ns, "Bookmark", value)),
        new("DocumentMapLabel", "Interactivity", "text", context => Get(context.Element, context.Ns, "DocumentMapLabel", ""), (context, value) => Put(context.Element, context.Ns, "DocumentMapLabel", value)),
        new("Hyperlink", "Interactivity", "text", context => ActionValue(context, "Hyperlink"), (context, value) => SetAction(context, "Hyperlink", value)),
        new("BookmarkLink", "Interactivity", "text", context => ActionValue(context, "BookmarkLink"), (context, value) => SetAction(context, "BookmarkLink", value)),
    ];

    /// <summary>Text box only: padding, duplicates and the +/- toggle image of a drilldown.</summary>
    private static readonly Definition[] TextBoxExtraDefinitions =
    [
        new("PaddingLeft", "Padding", "padding", context => Get(ItemStyle(context), context.Ns, "PaddingLeft", "2pt"), (context, value) => Put(ItemStyle(context), context.Ns, "PaddingLeft", value)),
        new("PaddingRight", "Padding", "padding", context => Get(ItemStyle(context), context.Ns, "PaddingRight", "2pt"), (context, value) => Put(ItemStyle(context), context.Ns, "PaddingRight", value)),
        new("PaddingTop", "Padding", "padding", context => Get(ItemStyle(context), context.Ns, "PaddingTop", "2pt"), (context, value) => Put(ItemStyle(context), context.Ns, "PaddingTop", value)),
        new("PaddingBottom", "Padding", "padding", context => Get(ItemStyle(context), context.Ns, "PaddingBottom", "2pt"), (context, value) => Put(ItemStyle(context), context.Ns, "PaddingBottom", value)),
        new("HideDuplicates", "Visibility", "select", context => Get(context.Element, context.Ns, "HideDuplicates", ""), (context, value) => Put(context.Element, context.Ns, "HideDuplicates", value), ScopeNames, Expression: false),
        new("InitialToggleState", "Visibility", "check", context => Get(context.Element.Element(context.Ns + "ToggleImage"), context.Ns, "InitialState", "false"), SetInitialToggleState),
    ];

    private static readonly Definition[] RectangleExtraDefinitions =
    [
        new("BreakLocation", "PageBreak", "select", context => Get(context.Element.Element(context.Ns + "PageBreak"), context.Ns, "BreakLocation", "None"), (context, value) => SetBreak(context, value), _ => BreakLocations, Expression: false),
        new("KeepTogether", "PageBreak", "check", context => Get(context.Element, context.Ns, "KeepTogether", "false"), (context, value) => Put(context.Element, context.Ns, "KeepTogether", value), Expression: false),
    ];

    private static IReadOnlyList<string> ToggleCandidates(Context context)
    {
        var self = Name(context);
        return
        [
            "",
            .. context.Document.Root.Descendants(context.Ns + "Textbox")
                .Select(textbox => textbox.Attribute("Name")?.Value ?? "")
                .Where(name => name.Length > 0 && !string.Equals(name, self, StringComparison.Ordinal)),
        ];
    }

    /// <summary>Datasets, data regions and groups: the scopes a text box can hide duplicates within.</summary>
    private static IReadOnlyList<string> ScopeNames(Context context) =>
    [
        "",
        .. context.Document.Root.Descendants()
            .Where(element => element.Name.LocalName is "DataSet" or "Tablix" or "Group" && element.Attribute("Name") is not null)
            .Select(element => element.Attribute("Name")!.Value)
            .Distinct(StringComparer.Ordinal),
    ];

    private static void SetToggleItem(Context context, string? value)
    {
        var visibility = context.Element.Element(context.Ns + "Visibility");
        if (string.IsNullOrEmpty(value))
        {
            visibility?.Element(context.Ns + "ToggleItem")?.Remove();
            if (visibility is not null && !visibility.HasElements)
            {
                visibility.Remove();
            }

            return;
        }

        Put(visibility ?? Child(context.Element, context.Ns, "Visibility"), context.Ns, "ToggleItem", value);
    }

    private static void SetInitialToggleState(Context context, string? value)
    {
        if (value is null or "false")
        {
            context.Element.Element(context.Ns + "ToggleImage")?.Remove();
            return;
        }

        Put(Child(context.Element, context.Ns, "ToggleImage"), context.Ns, "InitialState", value);
    }

    private static void SetBreak(Context context, string? value)
    {
        if (value is null or "None")
        {
            context.Element.Element(context.Ns + "PageBreak")?.Remove();
            return;
        }

        Put(Child(context.Element, context.Ns, "PageBreak"), context.Ns, "BreakLocation", value);
    }

    private static string ActionValue(Context context, string kind) =>
        context.Element.Element(context.Ns + "ActionInfo")?.Element(context.Ns + "Actions")?.Elements(context.Ns + "Action").FirstOrDefault()?.Element(context.Ns + kind)?.Value ?? "";

    /// <summary>One action per item: setting a hyperlink replaces a bookmark link and the other way round.</summary>
    private static void SetAction(Context context, string kind, string? value)
    {
        var ns = context.Ns;
        context.Element.Element(ns + "ActionInfo")?.Remove();
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        context.Element.Add(new XElement(ns + "ActionInfo", new XElement(ns + "Actions", new XElement(ns + "Action", new XElement(ns + kind, value)))));
    }

    private static readonly Definition[] ReportDefinitions =
    [
        new("PaperSize", "PaperSize", "select", context => PaperSize(context), (context, value) => SetPaperSize(context, value ?? "Letter"), _ => [.. PaperSizes.Select(size => size.Name), "Custom"], Expression: false),
        new("Orientation", "PaperSize", "select", context => PageLength(context, "PageWidth") > PageLength(context, "PageHeight") ? "Landscape" : "Portrait", (context, value) => SetOrientation(context, value ?? "Portrait"), _ => Orientations, Expression: false),
        new("PageWidth", "PaperSize", "length", context => Get(context.Page, context.Ns, "PageWidth", "8.5in"), (context, value) => PutRequired(context.Page, context.Ns, "PageWidth", value), Expression: false),
        new("PageHeight", "PaperSize", "length", context => Get(context.Page, context.Ns, "PageHeight", "11in"), (context, value) => PutRequired(context.Page, context.Ns, "PageHeight", value), Expression: false),
        new("LeftMargin", "Margin", "length", context => Get(context.Page, context.Ns, "LeftMargin", "0in"), (context, value) => Put(context.Page, context.Ns, "LeftMargin", value), Expression: false),
        new("RightMargin", "Margin", "length", context => Get(context.Page, context.Ns, "RightMargin", "0in"), (context, value) => Put(context.Page, context.Ns, "RightMargin", value), Expression: false),
        new("TopMargin", "Margin", "length", context => Get(context.Page, context.Ns, "TopMargin", "0in"), (context, value) => Put(context.Page, context.Ns, "TopMargin", value), Expression: false),
        new("BottomMargin", "Margin", "length", context => Get(context.Page, context.Ns, "BottomMargin", "0in"), (context, value) => Put(context.Page, context.Ns, "BottomMargin", value), Expression: false),
        new("Columns", "PageColumn", "integer", context => Get(context.Page, context.Ns, "Columns", "1"), (context, value) => Put(context.Page, context.Ns, "Columns", value), Expression: false),
        new("ColumnSpacing", "PageColumn", "length", context => Get(context.Page, context.Ns, "ColumnSpacing", "0.5in"), (context, value) => Put(context.Page, context.Ns, "ColumnSpacing", value), Expression: false),
        new("ReportWidth", "Basic", "length", context => Get(ReportSection(context), context.Ns, "Width", "6.5in"), (context, value) => PutRequired(ReportSection(context), context.Ns, "Width", value), Expression: false),
        new("BackgroundColor", "Basic", "color", context => Get(Child(context.Page, context.Ns, "Style"), context.Ns, "BackgroundColor", ""), (context, value) => Put(Child(context.Page, context.Ns, "Style"), context.Ns, "BackgroundColor", value)),
        new("Author", "Miscellaneous", "text", context => Get(context.Element, context.Ns, "Author", ""), (context, value) => Put(context.Element, context.Ns, "Author", value), Expression: false),
        new("Description", "Miscellaneous", "text", context => Get(context.Element, context.Ns, "Description", ""), (context, value) => Put(context.Element, context.Ns, "Description", value), Expression: false),
        new("Language", "Miscellaneous", "text", context => Get(context.Element, context.Ns, "Language", ""), (context, value) => Put(context.Element, context.Ns, "Language", value)),
    ];

    private static readonly Definition[] BandDefinitions =
    [
        new("BandHeight", "Basic", "length", context => Get(context.Element, context.Ns, "Height", "0.5in"), (context, value) => PutRequired(context.Element, context.Ns, "Height", value), Expression: false),
        new("PrintOnFirstPage", "Basic", "check", context => Get(context.Element, context.Ns, "PrintOnFirstPage", "true"), (context, value) => Put(context.Element, context.Ns, "PrintOnFirstPage", value), Expression: false),
        new("PrintOnLastPage", "Basic", "check", context => Get(context.Element, context.Ns, "PrintOnLastPage", "true"), (context, value) => Put(context.Element, context.Ns, "PrintOnLastPage", value), Expression: false),
        new("BackgroundColor", "Appearance", "color", context => Get(Child(context.Element, context.Ns, "Style"), context.Ns, "BackgroundColor", ""), (context, value) => Put(Child(context.Element, context.Ns, "Style"), context.Ns, "BackgroundColor", value)),
    ];

    private static string Name(Context context) => context.Element.Attribute("Name")?.Value ?? "";

    private static XElement Child(XElement parent, XNamespace ns, string name)
    {
        var child = parent.Element(ns + name);
        if (child is null)
        {
            child = new XElement(ns + name);
            parent.Add(child);
        }

        return child;
    }

    private static string Get(XElement? parent, XNamespace ns, string name, string fallback) =>
        parent?.Element(ns + name)?.Value is { Length: > 0 } value ? value : fallback;

    private static void Put(XElement parent, XNamespace ns, string name, string? value)
    {
        var child = parent.Element(ns + name);
        if (value is null)
        {
            child?.Remove();
            return;
        }

        if (child is null)
        {
            parent.Add(new XElement(ns + name, value));
        }
        else
        {
            child.Value = value;
        }
    }

    private static void PutRequired(XElement parent, XNamespace ns, string name, string? value) =>
        Put(parent, ns, name, value ?? throw new ReportDesignException(ReportCodes.InvalidValue));

    private static XElement ItemStyle(Context context) => Child(context.Element, context.Ns, "Style");

    private static XElement Border(Context context) => Child(ItemStyle(context), context.Ns, "Border");

    private static XElement RunStyle(Context context)
    {
        var run = context.Element.Descendants(context.Ns + "TextRun").FirstOrDefault() ?? throw new ReportDesignException(ReportCodes.InvalidItem);
        return Child(run, context.Ns, "Style");
    }

    private static XElement ParagraphStyle(Context context)
    {
        var paragraph = context.Element.Descendants(context.Ns + "Paragraph").FirstOrDefault() ?? throw new ReportDesignException(ReportCodes.InvalidItem);
        return Child(paragraph, context.Ns, "Style");
    }

    private static XElement? FirstCellRun(Context context) =>
        context.Element.Descendants(context.Ns + "TextRun").FirstOrDefault()?.Element(context.Ns + "Style");

    private static void PutAllCells(Context context, string name, string? value)
    {
        foreach (var run in context.Element.Descendants(context.Ns + "TextRun"))
        {
            Put(Child(run, context.Ns, "Style"), context.Ns, name, value);
        }
    }

    private static string RunValue(XElement element, XNamespace ns) =>
        element.Descendants(ns + "TextRun").FirstOrDefault()?.Element(ns + "Value")?.Value ?? "";

    private static void SetRunValue(Context context, string value)
    {
        var run = context.Element.Descendants(context.Ns + "TextRun").FirstOrDefault() ?? throw new ReportDesignException(ReportCodes.InvalidItem);
        Put(run, context.Ns, "Value", value.Length > 2000 ? value[..2000] : value);
    }

    private static void SetHidden(Context context, string? value)
    {
        var visibility = context.Element.Element(context.Ns + "Visibility");
        if (value is null or "false")
        {
            visibility?.Element(context.Ns + "Hidden")?.Remove();
            if (visibility is not null && !visibility.HasElements)
            {
                visibility.Remove();
            }

            return;
        }

        Put(visibility ?? Child(context.Element, context.Ns, "Visibility"), context.Ns, "Hidden", value);
    }

    private static double ItemWidth(Context context) =>
        context.Kind == "Tablix"
            ? TablixNavigator.Columns(context.Element, context.Ns).Sum(column => Dimension.ToInches(column.Element(context.Ns + "Width")?.Value))
            : Dimension.ToInches(context.Element.Element(context.Ns + "Width")?.Value);

    private static double ItemHeight(Context context) =>
        context.Kind == "Tablix"
            ? TablixNavigator.Rows(context.Element, context.Ns).Sum(row => Dimension.ToInches(row.Element(context.Ns + "Height")?.Value))
            : Dimension.ToInches(context.Element.Element(context.Ns + "Height")?.Value);

    private static void Move(Context context, string? left = null, string? top = null, string? width = null, string? height = null) =>
        ReportLayoutEditor.Move(
            context.Document,
            Name(context),
            left is null ? Dimension.ToInches(context.Element.Element(context.Ns + "Left")?.Value) : Dimension.ToInches(left),
            top is null ? Dimension.ToInches(context.Element.Element(context.Ns + "Top")?.Value) : Dimension.ToInches(top),
            width is null ? ItemWidth(context) : Dimension.ToInches(width),
            height is null ? ItemHeight(context) : Dimension.ToInches(height));

    private static IEnumerable<string> FieldNames(Context context) =>
        ReportDataSetEditor.Fields(context.Document, context.Element.Element(context.Ns + "DataSetName")?.Value).Select(field => field.Name);

    private static DesignChart Chart(Context context) => ReportChartEditor.Read(context.Element, context.Ns);

    private static void EditChart(Context context, Func<DesignChart, DesignChart> change, string? palette = null)
    {
        var current = Chart(context);
        if (!current.Editable)
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        var next = change(current);
        var dataSet = context.Element.Element(context.Ns + "DataSetName")?.Value;
        ReportChartEditor.Edit(
            context.Document,
            new ReportChartEdit(Name(context), next.ChartType, next.Title, next.Category, next.Series, next.ValueField, next.Aggregate, next.ShowLegend, next.ShowLabels, palette, next.SecondField),
            dataSet,
            ReportDataSetEditor.Fields(context.Document, dataSet));
    }

    private static void SetChartDataSet(Context context, string? value)
    {
        if (value is null)
        {
            throw new ReportDesignException(ReportCodes.InvalidValue);
        }

        var current = Chart(context);
        ReportDataSetEditor.Bind(context.Document, Name(context), value);
        var fields = ReportDataSetEditor.Fields(context.Document, value);
        if (fields.Count == 0 || !current.Editable)
        {
            return;
        }

        var names = fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        var category = names.Contains(current.Category) ? current.Category : fields[0].Name;
        var valueField = names.Contains(current.ValueField) ? current.ValueField : fields[0].Name;
        var series = names.Contains(current.Series) ? current.Series : "";
        ReportChartEditor.Edit(
            context.Document,
            new ReportChartEdit(Name(context), current.ChartType, current.Title, category, series, valueField, current.Aggregate, current.ShowLegend, current.ShowLabels),
            value,
            fields);
    }

    private static bool RepeatsHeader(Context context)
    {
        var first = context.Element.Element(context.Ns + "TablixRowHierarchy")?.Element(context.Ns + "TablixMembers")?.Elements(context.Ns + "TablixMember").FirstOrDefault();
        return string.Equals(first?.Element(context.Ns + "RepeatOnNewPage")?.Value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The details group: the row member whose group has no group expression.</summary>
    private static XElement? DetailsGroup(Context context) =>
        context.Element.Element(context.Ns + "TablixRowHierarchy")?
            .Descendants(context.Ns + "Group")
            .FirstOrDefault(group => group.Element(context.Ns + "GroupExpressions") is null);

    private static (string Field, string Direction) TableSort(Context context)
    {
        var member = DetailsGroup(context)?.Parent;
        var sort = member?.Element(context.Ns + "SortExpressions")?.Elements(context.Ns + "SortExpression").FirstOrDefault();
        var value = sort?.Element(context.Ns + "Value")?.Value ?? "";
        const string prefix = "=Fields!";
        var field = value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith(".Value", StringComparison.Ordinal)
            ? value[prefix.Length..^".Value".Length]
            : "";
        return (field, sort?.Element(context.Ns + "Direction")?.Value ?? "Ascending");
    }

    private static void SetTableSort(Context context, string? field, string? direction)
    {
        var member = DetailsGroup(context)?.Parent ?? throw new ReportDesignException(ReportCodes.InvalidItem);
        var current = TableSort(context);
        var nextField = field ?? (direction is null ? "" : current.Field);
        var nextDirection = direction ?? current.Direction;
        member.Element(context.Ns + "SortExpressions")?.Remove();
        if (nextField.Length == 0)
        {
            return;
        }

        var sorts = new XElement(
            context.Ns + "SortExpressions",
            new XElement(
                context.Ns + "SortExpression",
                new XElement(context.Ns + "Value", "=Fields!" + nextField + ".Value"),
                new XElement(context.Ns + "Direction", nextDirection)));
        var group = member.Element(context.Ns + "Group");
        if (group is null)
        {
            member.AddFirst(sorts);
        }
        else
        {
            group.AddAfterSelf(sorts);
        }
    }

    private static XElement ReportSection(Context context) => context.Page.Parent ?? throw new ReportDesignException(ReportCodes.InvalidDefinition);

    private static double PageLength(Context context, string name) => Dimension.ToInches(context.Page.Element(context.Ns + name)?.Value);

    private static string PaperSize(Context context)
    {
        var width = PageLength(context, "PageWidth");
        var height = PageLength(context, "PageHeight");
        var shortSide = Math.Min(width, height);
        var longSide = Math.Max(width, height);
        foreach (var (name, w, h) in PaperSizes)
        {
            if (Math.Abs(shortSide - w) < 0.05 && Math.Abs(longSide - h) < 0.05)
            {
                return name;
            }
        }

        return "Custom";
    }

    private static void SetPaperSize(Context context, string name)
    {
        if (name == "Custom")
        {
            return;
        }

        var size = PaperSizes.First(candidate => candidate.Name == name);
        var landscape = PageLength(context, "PageWidth") > PageLength(context, "PageHeight");
        Put(context.Page, context.Ns, "PageWidth", Dimension.FromInches(landscape ? size.Height : size.Width));
        Put(context.Page, context.Ns, "PageHeight", Dimension.FromInches(landscape ? size.Width : size.Height));
    }

    private static void SetOrientation(Context context, string orientation)
    {
        var width = PageLength(context, "PageWidth");
        var height = PageLength(context, "PageHeight");
        var landscape = orientation == "Landscape";
        if (landscape == width > height)
        {
            return;
        }

        Put(context.Page, context.Ns, "PageWidth", Dimension.FromInches(height));
        Put(context.Page, context.Ns, "PageHeight", Dimension.FromInches(width));
    }

    private sealed record Context(RdlDocument Document, XElement Element, XNamespace Ns, string Kind, XElement Page);

    private sealed record Definition(
        string Key,
        string Section,
        string Editor,
        Getter Get,
        Setter Set,
        OptionSource? Options = null,
        bool Expression = true,
        Func<Context, bool>? When = null);
}
