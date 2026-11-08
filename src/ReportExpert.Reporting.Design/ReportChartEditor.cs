using System.Text.RegularExpressions;
using System.Xml.Linq;
using ReportExpert.Rdl.Core;

namespace ReportExpert.Reporting.Design;

/// <summary>
/// Creates and rebinds single-series RDL charts: one category group, an optional series group,
/// and one aggregated value. Imported charts with a richer structure are read but not rewritten.
/// </summary>
internal static partial class ReportChartEditor
{
    /// <summary>
    /// Chart types offered as separate toolbox tiles, each mapped to the RDL series <c>Type</c> and <c>Subtype</c>.
    /// <c>Markers</c> adds point markers to a line.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Type, string Subtype, bool Markers)> Kinds =
    [
        ("Column", "Column", "Plain", false),
        ("StackedColumn", "Column", "Stacked", false),
        ("PercentColumn", "Column", "PercentStacked", false),
        ("Bar", "Bar", "Plain", false),
        ("StackedBar", "Bar", "Stacked", false),
        ("PercentBar", "Bar", "PercentStacked", false),
        ("Line", "Line", "Plain", false),
        ("SmoothLine", "Line", "Smooth", false),
        ("SteppedLine", "Line", "Stepped", false),
        ("LineMarkers", "Line", "Plain", true),
        ("Area", "Area", "Plain", false),
        ("SmoothArea", "Area", "Smooth", false),
        ("StackedArea", "Area", "Stacked", false),
        ("PercentArea", "Area", "PercentStacked", false),
        ("Pie", "Shape", "Pie", false),
        ("ExplodedPie", "Shape", "ExplodedPie", false),
        ("Doughnut", "Shape", "Doughnut", false),
        ("Funnel", "Shape", "Funnel", false),
        ("Pyramid", "Shape", "Pyramid", false),
        ("Polar", "Polar", "Plain", false),
        ("Radar", "Polar", "Radar", false),
        ("Scatter", "Scatter", "Plain", true),
        ("Bubble", "Scatter", "Bubble", true),
        ("RangeColumn", "Range", "Column", false),
        ("RangeBar", "Range", "Bar", false),
        ("RangeArea", "Range", "Plain", false),
        ("SmoothRangeArea", "Range", "Smooth", false),
    ];

    /// <summary>Axis positions offered for label rotation; <c>Auto</c> removes the angle.</summary>
    public static readonly string[] Angles = ["Auto", "0", "-45", "-90", "45", "90"];

    public static readonly string[] LegendPositions = ["TopCenter", "TopLeft", "TopRight", "BottomCenter", "BottomLeft", "BottomRight", "LeftCenter", "RightCenter"];

    public static readonly string[] ChartTypes = Kinds.Select(kind => kind.Key).ToArray();

    public static readonly string[] Palettes = ["Pacific", "BrightPastel", "Berry", "Chocolate", "EarthTones", "Excel", "Fire", "GrayScale", "Light", "Pastel", "SeaGreen", "SemiTransparent"];

    public static readonly string[] Aggregates = ["Count", "CountDistinct", "Sum", "Avg", "Min", "Max"];

    private static readonly string[] NumericTypes =
    [
        "System.Int16", "System.Int32", "System.Int64", "System.Decimal", "System.Double", "System.Single",
        "System.Byte", "System.UInt16", "System.UInt32", "System.UInt64",
    ];

    private static readonly string[] KeptElements = ["Top", "Left", "Height", "Width", "Visibility", "ZIndex", "Style", "ToolTip", "Bookmark"];

    public static string Add(RdlDocument document, XElement parent, double left, double top, string? dataSetName, IReadOnlyList<ReportField> fields, string chartType = "Column")
    {
        if (string.IsNullOrEmpty(dataSetName) || fields.Count == 0)
        {
            throw new ReportDesignException(ReportCodes.NeedDataSet);
        }

        var ns = document.Ns;
        var name = ReportLayoutEditor.NextName(document, "Chart");
        var category = fields[0].Name;
        var type = ChartTypes.Contains(chartType, StringComparer.Ordinal) ? chartType : "Column";
        var numeric = fields.FirstOrDefault(field => NumericTypes.Contains(field.DataType, StringComparer.Ordinal));
        var second = fields.Where(field => NumericTypes.Contains(field.DataType, StringComparer.Ordinal)).Skip(1).FirstOrDefault() ?? numeric;
        var spec = numeric is null
            ? new DesignChart(type, "", category, "", category, "Count", ShowLegend: IsShape(type), ShowLabels: false, Editable: true, SecondField: NeedsSecond(type) ? category : "")
            : new DesignChart(type, "", category, "", numeric.Name, "Sum", ShowLegend: IsShape(type), ShowLabels: false, Editable: true, SecondField: NeedsSecond(type) ? second!.Name : "");
        var chart = Build(ns, name, spec, dataSetName, fields);
        chart.Add(
            new XElement(ns + "Top", Dimension.FromInches(top)),
            new XElement(ns + "Left", Dimension.FromInches(left)),
            new XElement(ns + "Height", "2.50in"),
            new XElement(ns + "Width", "4.50in"),
            new XElement(
                ns + "Style",
                new XElement(ns + "Border", new XElement(ns + "Style", "None")),
                new XElement(ns + "BackgroundColor", "White")));
        parent.Add(chart);
        ReportLayoutEditor.FitBody(document);
        return name;
    }

    public static void Edit(RdlDocument document, ReportChartEdit edit, string? dataSetName, IReadOnlyList<ReportField> fields)
    {
        var ns = document.Ns;
        var chart = document.Root.Descendants(ns + "Chart")
            .FirstOrDefault(candidate => string.Equals(candidate.Attribute("Name")?.Value, edit.Name, StringComparison.Ordinal))
            ?? throw new ReportDesignException(ReportCodes.InvalidItem);
        var previous = Read(chart, ns);
        if (!previous.Editable)
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        if (string.IsNullOrEmpty(dataSetName) || fields.Count == 0)
        {
            throw new ReportDesignException(ReportCodes.NeedDataSet);
        }

        var known = fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        var category = edit.Category ?? "";
        var valueField = edit.ValueField ?? "";
        var series = edit.Series ?? "";
        if (!known.Contains(category) || !known.Contains(valueField) || (series.Length > 0 && !known.Contains(series)))
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        var chartType = ChartTypes.Contains(edit.ChartType ?? "", StringComparer.Ordinal) ? edit.ChartType! : "Column";
        var secondField = edit.SecondField ?? "";
        if (NeedsSecond(chartType))
        {
            if (secondField.Length == 0)
            {
                secondField = previous.SecondField is { Length: > 0 } kept && known.Contains(kept)
                    ? kept
                    : fields.FirstOrDefault(field => field.Name != valueField && NumericTypes.Contains(field.DataType, StringComparer.Ordinal))?.Name ?? valueField;
            }

            if (!known.Contains(secondField))
            {
                throw new ReportDesignException(ReportCodes.InvalidItem);
            }
        }
        else
        {
            secondField = "";
        }

        var title = (edit.Title ?? "").Trim();
        if (title.Length > 120)
        {
            title = title[..120];
        }

        var spec = new DesignChart(
            chartType,
            title,
            category,
            series,
            valueField,
            Aggregates.Contains(edit.Aggregate ?? "", StringComparer.Ordinal) ? edit.Aggregate! : "Count",
            edit.ShowLegend,
            edit.ShowLabels,
            Editable: true,
            SecondField: secondField);
        var rebuilt = Build(ns, edit.Name, spec, dataSetName, fields, edit.Palette ?? Palette(chart, ns));
        rebuilt.Add(chart.Elements().Where(element => KeptElements.Contains(element.Name.LocalName, StringComparer.Ordinal)).ToArray());
        CarryOver(chart, rebuilt, ns, previous, spec);
        chart.ReplaceWith(rebuilt);
    }

    public static DesignChart Read(XElement chart, XNamespace ns)
    {
        var categories = chart.Element(ns + "ChartCategoryHierarchy")?.Element(ns + "ChartMembers")?.Elements(ns + "ChartMember").ToArray() ?? [];
        var seriesMembers = chart.Element(ns + "ChartSeriesHierarchy")?.Element(ns + "ChartMembers")?.Elements(ns + "ChartMember").ToArray() ?? [];
        var collection = chart.Element(ns + "ChartData")?.Element(ns + "ChartSeriesCollection")?.Elements(ns + "ChartSeries").ToArray() ?? [];
        var first = collection.FirstOrDefault();
        var type = first?.Element(ns + "Type")?.Value ?? "Column";
        var subtype = first?.Element(ns + "Subtype")?.Value ?? "";
        var markers = first?.Element(ns + "ChartDataPoints")?.Element(ns + "ChartDataPoint")?.Element(ns + "ChartMarker")?.Element(ns + "Type")?.Value is { Length: > 0 } marker
            && !string.Equals(marker, "None", StringComparison.OrdinalIgnoreCase);
        var chartType = Kinds.FirstOrDefault(kind => kind.Type == type && kind.Subtype == (subtype.Length == 0 ? "Plain" : subtype) && kind.Markers == markers).Key
            ?? Kinds.FirstOrDefault(kind => kind.Type == type).Key
            ?? "Column";

        var point = first?.Element(ns + "ChartDataPoints")?.Element(ns + "ChartDataPoint");
        var values = point?.Element(ns + "ChartDataPointValues");
        var y = values?.Element(ns + "Y")?.Value ?? values?.Element(ns + "Low")?.Value ?? "";
        var secondText = values?.Element(ns + "High")?.Value ?? values?.Element(ns + "Size")?.Value ?? "";
        var match = ValueExpression().Match(y);
        var secondMatch = ValueExpression().Match(secondText);
        var aggregate = match.Success ? match.Groups["aggregate"].Value : "";
        var valueField = match.Success ? match.Groups["field"].Value : "";
        var category = FieldOf(categories.FirstOrDefault()?.Element(ns + "Group"), ns);
        var series = FieldOf(seriesMembers.FirstOrDefault()?.Element(ns + "Group"), ns);
        var titleElement = chart.Element(ns + "ChartTitles")?.Elements(ns + "ChartTitle").FirstOrDefault();
        var title = Hidden(titleElement, ns) ? "" : titleElement?.Element(ns + "Caption")?.Value ?? "";
        var legend = chart.Element(ns + "ChartLegends")?.Elements(ns + "ChartLegend").FirstOrDefault();
        var labels = point?.Element(ns + "ChartDataLabel")?.Element(ns + "Visible")?.Value;
        var editable = collection.Length == 1
            && categories.Length == 1
            && seriesMembers.Length == 1
            && categories[0].Element(ns + "ChartMembers") is null
            && seriesMembers[0].Element(ns + "ChartMembers") is null
            && category.Length > 0
            && match.Success
            && Aggregates.Contains(aggregate, StringComparer.Ordinal)
            && (secondText.Length == 0 || secondMatch.Success);
        return new DesignChart(
            chartType,
            title,
            category,
            series,
            valueField,
            aggregate.Length == 0 ? "Count" : aggregate,
            legend is not null && !Hidden(legend, ns),
            string.Equals(labels, "true", StringComparison.OrdinalIgnoreCase),
            editable,
            secondMatch.Success ? secondMatch.Groups["field"].Value : "");
    }

    private static XElement Build(XNamespace ns, string name, DesignChart spec, string dataSetName, IReadOnlyList<ReportField> fields, string palette = "Pacific")
    {
        var kind = Kinds.FirstOrDefault(candidate => candidate.Key == spec.ChartType);
        if (kind.Key is null)
        {
            kind = Kinds[0];
        }

        var (type, subtype) = (kind.Type, kind.Subtype);
        var shape = type == "Shape";
        var categoryValue = "=Fields!" + spec.Category + ".Value";
        var y = Expression(spec.Aggregate, spec.ValueField, TypeOf(fields, spec.ValueField));
        var pointValues = new XElement(ns + "ChartDataPointValues");
        var categoryType = TypeOf(fields, spec.Category);
        var scalar = type == "Scatter" && (NumericTypes.Contains(categoryType, StringComparer.Ordinal) || categoryType == "System.DateTime");
        if (type == "Range")
        {
            pointValues.Add(
                new XElement(ns + "Low", y),
                new XElement(ns + "High", Expression(spec.Aggregate, Second(spec), TypeOf(fields, Second(spec)))));
        }
        else
        {
            if (scalar)
            {
                pointValues.Add(new XElement(ns + "X", categoryValue));
            }

            pointValues.Add(new XElement(ns + "Y", y));
            if (subtype == "Bubble")
            {
                pointValues.Add(new XElement(ns + "Size", Expression(spec.Aggregate, Second(spec), TypeOf(fields, Second(spec)))));
            }
        }

        var seriesMember = spec.Series.Length == 0
            ? new XElement(ns + "ChartMember", new XElement(ns + "Label", spec.Aggregate + " " + spec.ValueField))
            : new XElement(
                ns + "ChartMember",
                Group(ns, name + "_Series", "=Fields!" + spec.Series + ".Value"),
                Sort(ns, "=Fields!" + spec.Series + ".Value"),
                new XElement(ns + "Label", "=Fields!" + spec.Series + ".Value"));

        var chart = new XElement(
            ns + "Chart",
            new XAttribute("Name", name),
            new XElement(
                ns + "ChartCategoryHierarchy",
                new XElement(
                    ns + "ChartMembers",
                    new XElement(
                        ns + "ChartMember",
                        Group(ns, name + "_Category", categoryValue),
                        Sort(ns, categoryValue),
                        new XElement(ns + "Label", categoryValue)))),
            new XElement(ns + "ChartSeriesHierarchy", new XElement(ns + "ChartMembers", seriesMember)),
            new XElement(
                ns + "ChartData",
                new XElement(
                    ns + "ChartSeriesCollection",
                    new XElement(
                        ns + "ChartSeries",
                        new XAttribute("Name", name + "_Values"),
                        new XElement(
                            ns + "ChartDataPoints",
                            new XElement(
                                ns + "ChartDataPoint",
                                pointValues,
                                new XElement(
                                    ns + "ChartDataLabel",
                                    new XElement(ns + "Style", new XElement(ns + "FontSize", "8pt")),
                                    new XElement(ns + "UseValueAsLabel", "true"),
                                    new XElement(ns + "Visible", spec.ShowLabels ? "true" : "false")),
                                new XElement(ns + "Style"),
                                new XElement(ns + "ChartMarker", new XElement(ns + "Type", kind.Markers ? "Auto" : "None"), new XElement(ns + "Style")),
                                new XElement(ns + "DataElementOutput", "Output"))),
                        new XElement(ns + "Type", type),
                        new XElement(ns + "Subtype", subtype),
                        new XElement(ns + "Style"),
                        new XElement(ns + "ValueAxisName", "Primary"),
                        new XElement(ns + "CategoryAxisName", "Primary"),
                        new XElement(
                            ns + "ChartSmartLabel",
                            new XElement(ns + "CalloutLineColor", "Black"),
                            new XElement(ns + "MinMovingDistance", "0pt"))))),
            new XElement(
                ns + "ChartAreas",
                new XElement(
                    ns + "ChartArea",
                    new XAttribute("Name", "Default"),
                    new XElement(ns + "ChartCategoryAxes", Axis(ns, shape ? "" : AutoCategoryTitle(spec), gridLines: false, shape, scalar)),
                    new XElement(ns + "ChartValueAxes", Axis(ns, shape ? "" : AutoValueTitle(spec), gridLines: !shape, shape)),
                    new XElement(ns + "Style", new XElement(ns + "BackgroundGradientType", "None")))),
            new XElement(
                ns + "ChartLegends",
                new XElement(
                    ns + "ChartLegend",
                    new XAttribute("Name", "Default"),
                    new XElement(ns + "Hidden", spec.ShowLegend ? "false" : "true"),
                    new XElement(ns + "Style", new XElement(ns + "FontSize", "8pt")),
                    new XElement(ns + "Position", "BottomCenter"),
                    new XElement(ns + "ChartLegendTitle", new XElement(ns + "Caption"), new XElement(ns + "Style")),
                    new XElement(ns + "HeaderSeparatorColor", "Black"),
                    new XElement(ns + "ColumnSeparatorColor", "Black"))),
            new XElement(ns + "Palette", Palettes.Contains(palette, StringComparer.Ordinal) ? palette : "Pacific"),
            new XElement(
                ns + "ChartBorderSkin",
                new XElement(
                    ns + "Style",
                    new XElement(ns + "BackgroundColor", "Gray"),
                    new XElement(ns + "BackgroundGradientType", "None"),
                    new XElement(ns + "Color", "White"))),
            new XElement(
                ns + "ChartNoDataMessage",
                new XAttribute("Name", "NoDataMessage"),
                new XElement(ns + "Caption", "No data"),
                new XElement(ns + "Style", new XElement(ns + "FontSize", "9pt"))),
            new XElement(ns + "DataSetName", dataSetName));
        if (spec.Title.Length > 0)
        {
            chart.Add(new XElement(
                ns + "ChartTitles",
                new XElement(
                    ns + "ChartTitle",
                    new XAttribute("Name", "Default"),
                    new XElement(ns + "Caption", spec.Title),
                    new XElement(
                        ns + "Style",
                        new XElement(ns + "FontSize", "11pt"),
                        new XElement(ns + "FontWeight", "Bold")))));
        }

        return chart;
    }

    /// <summary>
    /// Built-in providers return text columns, so Sum, Avg, Min and Max convert with <c>Val</c>,
    /// which reads an empty or non-numeric cell as zero instead of failing the render.
    /// </summary>
    private static string Expression(string aggregate, string field, string dataType)
    {
        var reference = "Fields!" + field + ".Value";
        if (aggregate is "Count" or "CountDistinct" || NumericTypes.Contains(dataType, StringComparer.Ordinal))
        {
            return "=" + aggregate + "(" + reference + ")";
        }

        return "=" + aggregate + "(Val(" + reference + "))";
    }

    private static XElement Group(XNamespace ns, string name, string expression) =>
        new(
            ns + "Group",
            new XAttribute("Name", name),
            new XElement(ns + "GroupExpressions", new XElement(ns + "GroupExpression", expression)));

    private static XElement Sort(XNamespace ns, string expression) =>
        new(ns + "SortExpressions", new XElement(ns + "SortExpression", new XElement(ns + "Value", expression)));

    private static XElement Axis(XNamespace ns, string title, bool gridLines, bool shape, bool scalar = false)
    {
        var axis = new XElement(
            ns + "ChartAxis",
            new XAttribute("Name", "Primary"),
            new XElement(ns + "Style", new XElement(ns + "FontSize", "8pt")),
            new XElement(
                ns + "ChartAxisTitle",
                new XElement(ns + "Caption", title),
                new XElement(ns + "Style", new XElement(ns + "FontSize", "8pt"))),
            new XElement(
                ns + "ChartMajorGridLines",
                new XElement(ns + "Enabled", gridLines ? "True" : "False"),
                new XElement(ns + "Style", new XElement(ns + "Border", new XElement(ns + "Color", "Gainsboro")))));
        if (shape)
        {
            axis.Add(new XElement(ns + "Visible", "False"));
        }

        if (scalar)
        {
            axis.Add(new XElement(ns + "Scalar", "true"));
        }

        return axis;
    }

    /// <summary>
    /// Repairs, in the copy sent to the renderer, polar charts written before the subtype was corrected:
    /// the RDL subtype of a polar chart is <c>Plain</c>, and ReportViewer refuses <c>Polar</c>.
    /// </summary>
    public static void NormalizeForRender(RdlDocument document)
    {
        var ns = document.Ns;
        foreach (var series in document.Root.Descendants(ns + "ChartSeries"))
        {
            if (series.Element(ns + "Type")?.Value == "Polar" && series.Element(ns + "Subtype") is { Value: "Polar" } subtype)
            {
                subtype.Value = "Plain";
            }
        }
    }

    private static string AutoCategoryTitle(DesignChart spec) => spec.Category;

    private static string AutoValueTitle(DesignChart spec) => spec.Aggregate + " " + spec.ValueField;

    private static string Second(DesignChart spec) => spec.SecondField.Length > 0 ? spec.SecondField : spec.ValueField;

    private static string TypeOf(IReadOnlyList<ReportField> fields, string name) =>
        fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.Ordinal))?.DataType ?? "System.String";

    /// <summary>Range charts need a high value and bubble charts a size, both read from a second field.</summary>
    public static bool NeedsSecond(string chartType) =>
        Kinds.FirstOrDefault(kind => kind.Key == chartType) is var kind && (kind.Type == "Range" || kind.Subtype == "Bubble");

    /// <summary>
    /// Rebinding rebuilds the chart from its bindings. Settings made in the properties panel (axis scale, format,
    /// labels, grid lines, legend position, no-data message) are copied from the previous chart so they survive.
    /// An axis title that still has its automatic text follows the new bindings; a custom one is kept.
    /// </summary>
    private static void CarryOver(XElement previous, XElement rebuilt, XNamespace ns, DesignChart before, DesignChart after)
    {
        var oldLegend = previous.Element(ns + "ChartLegends")?.Element(ns + "ChartLegend");
        var newLegend = rebuilt.Element(ns + "ChartLegends")?.Element(ns + "ChartLegend");
        if (oldLegend?.Element(ns + "Position")?.Value is { Length: > 0 } position && newLegend is not null)
        {
            newLegend.SetElementValue(ns + "Position", position);
        }

        if (previous.Element(ns + "ChartNoDataMessage")?.Element(ns + "Caption")?.Value is { } caption)
        {
            rebuilt.Element(ns + "ChartNoDataMessage")?.SetElementValue(ns + "Caption", caption);
        }

        if (IsShape(before.ChartType) != IsShape(after.ChartType) || IsShape(after.ChartType))
        {
            return;
        }

        foreach (var (collection, oldAuto, newAuto) in new[]
        {
            ("ChartCategoryAxes", AutoCategoryTitle(before), AutoCategoryTitle(after)),
            ("ChartValueAxes", AutoValueTitle(before), AutoValueTitle(after)),
        })
        {
            var oldAxis = previous.Descendants(ns + collection).FirstOrDefault()?.Element(ns + "ChartAxis");
            var newAxis = rebuilt.Descendants(ns + collection).FirstOrDefault()?.Element(ns + "ChartAxis");
            if (oldAxis is null || newAxis is null)
            {
                continue;
            }

            var copy = new XElement(oldAxis);
            var title = copy.Element(ns + "ChartAxisTitle")?.Element(ns + "Caption");
            if (title is not null && (title.Value == oldAuto || title.Value.Length == 0))
            {
                title.Value = newAuto;
            }

            var scalar = newAxis.Element(ns + "Scalar");
            copy.Element(ns + "Scalar")?.Remove();
            if (scalar is not null)
            {
                copy.Add(new XElement(scalar));
            }

            newAxis.ReplaceWith(copy);
        }
    }

    /// <summary>The primary category or value axis of an editable chart, or null for a chart without one.</summary>
    public static XElement? AxisOf(XElement chart, XNamespace ns, bool category) =>
        chart.Element(ns + "ChartAreas")?
            .Elements(ns + "ChartArea").FirstOrDefault()?
            .Element(ns + (category ? "ChartCategoryAxes" : "ChartValueAxes"))?
            .Elements(ns + "ChartAxis").FirstOrDefault();

    private static string FieldOf(XElement? group, XNamespace ns)
    {
        var expression = group?.Element(ns + "GroupExpressions")?.Elements(ns + "GroupExpression").FirstOrDefault()?.Value ?? "";
        var match = FieldExpression().Match(expression);
        return match.Success ? match.Groups["field"].Value : "";
    }

    public static bool IsShape(string chartType) => Kinds.FirstOrDefault(kind => kind.Key == chartType).Type is "Shape";

    public static string Palette(XElement chart, XNamespace ns) => chart.Element(ns + "Palette")?.Value ?? "Pacific";

    private static bool Hidden(XElement? element, XNamespace ns) =>
        string.Equals(element?.Element(ns + "Hidden")?.Value, "true", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^=(?<aggregate>[A-Za-z]+)\((?:Val\()?Fields!(?<field>[A-Za-z_][A-Za-z0-9_]*)\.Value\)?\)$", RegexOptions.CultureInvariant)]
    private static partial Regex ValueExpression();

    [GeneratedRegex(@"^=Fields!(?<field>[A-Za-z_][A-Za-z0-9_]*)\.Value$", RegexOptions.CultureInvariant)]
    private static partial Regex FieldExpression();
}
