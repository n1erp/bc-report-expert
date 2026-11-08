using System.Xml.Linq;
using ReportExpert.Rdl.Core;
using ReportExpert.Rdl.Core.Reading;
using ReportExpert.Rdl.Core.Validation;

namespace ReportExpert.Reporting.Design;

internal static class ReportDocumentReader
{
    private static readonly string[] IgnoredIssues = ["No datasets found", "No Tablix (table) found"];

    private static readonly string[] Kinds = ["Textbox", "Rectangle", "Line", "Image", "Tablix", "Chart", "Gauge", "Map"];

    public static ReportDesignModel Read(StoredReport stored, string? selectedItem, IReadOnlyList<IReportDataProvider> providers)
    {
        var document = RdlDocument.Parse(stored.Definition);
        var ns = document.Ns;
        var items = new List<DesignItem>();
        var placement = new ContainerPlacement(document);
        foreach (var element in document.Root.Descendants())
        {
            if (element.Parent?.Name.LocalName != "ReportItems" || !Kinds.Contains(element.Name.LocalName, StringComparer.Ordinal))
            {
                continue;
            }

            var region = element.Ancestors().Any(ancestor => ancestor.Name.LocalName == "PageHeader")
                ? "Header"
                : element.Ancestors().Any(ancestor => ancestor.Name.LocalName == "PageFooter")
                    ? "Footer"
                    : "Body";
            var runStyle = element.Descendants(ns + "TextRun").FirstOrDefault()?.Element(ns + "Style");
            var paragraphStyle = element.Descendants(ns + "Paragraph").FirstOrDefault()?.Element(ns + "Style");
            var border = element.Descendants(ns + "Border").FirstOrDefault()?.Element(ns + "Style")?.Value;
            var table = ReadTable(element, ns);
            var width = Positive(Dimension.ToInches(element.Element(ns + "Width")?.Value), 1);
            var height = Dimension.ToInches(element.Element(ns + "Height")?.Value);
            if (table.Columns.Count > 0)
            {
                width = table.Columns.Sum(column => column.WidthInches) + HeaderSizes(element.Element(ns + "TablixRowHierarchy"), ns);
            }

            if (table.Rows.Count > 0)
            {
                height = table.Rows.Sum(row => row.HeightInches) + HeaderSizes(element.Element(ns + "TablixColumnHierarchy"), ns);
            }

            var designItem = new DesignItem(
                element.Attribute("Name")?.Value ?? "",
                element.Name.LocalName,
                region,
                Dimension.ToInches(element.Element(ns + "Left")?.Value),
                Dimension.ToInches(element.Element(ns + "Top")?.Value),
                width,
                height,
                element.Descendants(ns + "Value").FirstOrDefault()?.Value ?? "",
                runStyle?.Element(ns + "FontFamily")?.Value ?? "Arial",
                runStyle?.Element(ns + "FontSize")?.Value ?? "11pt",
                string.Equals(runStyle?.Element(ns + "FontWeight")?.Value, "Bold", StringComparison.OrdinalIgnoreCase),
                paragraphStyle?.Element(ns + "TextAlign")?.Value ?? "Left",
                string.IsNullOrEmpty(border) ? "None" : border,
                string.Equals(element.Element(ns + "Visibility")?.Element(ns + "Hidden")?.Value, "true", StringComparison.OrdinalIgnoreCase),
                table.Columns,
                table.Rows,
                table.RowGroups,
                table.ColumnGroups,
                table.RepeatHeader,
                element.Name.LocalName == "Chart" ? ReportChartEditor.Read(element, ns) : null,
                element.Element(ns + "DataSetName")?.Value,
                Variant(element, ns));
            var (offsetLeft, offsetTop, container) = placement.Of(element);
            designItem = designItem with { OffsetLeftInches = offsetLeft, OffsetTopInches = offsetTop, Container = container, Style = DesignTextStyle.Read(element) };
            items.Add(element.Name.LocalName == "Tablix" ? WithStructure(designItem, document, element) : designItem);
        }

        var datasetElements = document.Descendants("DataSet").ToArray();
        var datasets = DataSetReader.GetDataSets(document, fieldLimit: -1).DataSets
            .Select(dataset => new DesignDataSet(
                dataset.Name,
                ReportDataCommand.ProviderKey(dataset.CommandText),
                (dataset.Fields ?? []).Select(field => new DesignField(field.Name, field.Type)).ToArray(),
                ReportFilterEditor.Read(
                    datasetElements.FirstOrDefault(element => string.Equals(element.Attribute("Name")?.Value, dataset.Name, StringComparison.Ordinal)),
                    ns)))
            .ToArray();
        var parameters = ReportParameterEditor.ReadAll(document);
        var validation = RdlValidator.Validate(document);
        var body = document.Descendants("Body").FirstOrDefault();
        var pageSetup = ReportPageSetup.Read(document);
        var bodyWidth = body is null ? 7.5 : Positive(Dimension.ToInches(body.Parent?.Element(ns + "Width")?.Value), 7.5);
        var warnings = (validation.Issues ?? []).Where(issue => !IgnoredIssues.Contains(issue, StringComparer.Ordinal))
            .Concat(validation.Warnings ?? [])
            .Concat(ReportPageSetup.BlankPageWarning(pageSetup, bodyWidth) is { } blank ? [blank] : [])
            .ToArray();
        var page = document.Descendants("Page").FirstOrDefault();
        var selectedKind = string.IsNullOrEmpty(selectedItem) ? null : ReportPropertySheet.KindOf(document, selectedItem);
        var selected = selectedKind is null ? null : selectedItem;
        var target = selected ?? ReportPropertySheet.ReportTarget;
        return new ReportDesignModel(
            stored.Id,
            stored.Name,
            stored.Revision,
            body is null ? 7.5 : Positive(Dimension.ToInches(body.Parent?.Element(ns + "Width")?.Value), 7.5),
            body is null ? 2 : Positive(Dimension.ToInches(body.Element(ns + "Height")?.Value), 2),
            items,
            datasets,
            parameters,
            providers.Select(provider => new ProviderChoice(
                provider.Key,
                provider.ResourceKey,
                provider.Fields.Select(field => new DesignField(field.Name, field.DataType)).ToArray(),
                provider is IReportDataSourceInfo info ? info.Group : "REX",
                provider is IReportDataSourceInfo described ? described.Label : provider.ResourceKey,
                provider is IReportDataSourceInfo view && view.IsView)).ToArray(),
            selected,
            warnings,
            false,
            false,
            Dimension.ToInches(page?.Element(ns + "PageHeader")?.Element(ns + "Height")?.Value),
            Dimension.ToInches(page?.Element(ns + "PageFooter")?.Element(ns + "Height")?.Value),
            selectedKind ?? "Report",
            ReportPropertySheet.Read(document, target),
            ReportImageEditor.Read(document),
            ReportItemOperations.Explorer(document),
            false,
            Positive(Dimension.ToInches(page?.Element(ns + "PageWidth")?.Value), 8.5),
            Positive(Dimension.ToInches(page?.Element(ns + "PageHeight")?.Value), 11),
            page?.Element(ns + "PageHeader") is not null,
            page?.Element(ns + "PageFooter") is not null)
        {
            Page = pageSetup,
        };
    }

    private static (IReadOnlyList<DesignColumn> Columns, IReadOnlyList<DesignRow> Rows, IReadOnlyList<DesignGroup> RowGroups, IReadOnlyList<DesignGroup> ColumnGroups, bool RepeatHeader) ReadTable(XElement element, XNamespace ns)
    {
        if (element.Name.LocalName != "Tablix")
        {
            return ([], [], [], [], false);
        }

        var columns = TablixNavigator.Columns(element, ns);
        var rows = TablixNavigator.Rows(element, ns);
        var leaves = ReportTableEditor.LeafMembers(element.Element(ns + "TablixRowHierarchy")?.Element(ns + "TablixMembers"), ns);
        var designRows = new List<DesignRow>();
        for (var index = 0; index < rows.Count; index++)
        {
            var cellElements = TablixNavigator.Cells(rows[index], ns);
            var cells = cellElements
                .Select(cell => TablixNavigator.FirstValueText(cell, ns) ?? "")
                .ToArray();
            var cellNames = cellElements
                .Select(cell => cell.Descendants(ns + "Textbox").FirstOrDefault()?.Attribute("Name")?.Value ?? "")
                .ToArray();
            var role = index < leaves.Count ? ReportTableEditor.Role(leaves[index], ns, index) : index == 0 ? "Header" : "Detail";
            var height = Dimension.ToInches(rows[index].Element(ns + "Height")?.Value);
            designRows.Add(new DesignRow(index, role, height > 0 ? height : 0.25, cells, cellNames));
        }

        var header = designRows.FirstOrDefault(row => row.Role == "Header") ?? designRows.FirstOrDefault();
        var detail = designRows.FirstOrDefault(row => row.Role == "Detail") ?? designRows.LastOrDefault();
        var designColumns = new List<DesignColumn>();
        for (var index = 0; index < columns.Count; index++)
        {
            var width = Dimension.ToInches(columns[index].Element(ns + "Width")?.Value);
            designColumns.Add(new DesignColumn(
                index,
                CellAt(header, index),
                CellAt(detail, index),
                width > 0 ? width : 1));
        }

        var first = element.Element(ns + "TablixRowHierarchy")?.Element(ns + "TablixMembers")?.Elements(ns + "TablixMember").FirstOrDefault();
        var repeat = string.Equals(first?.Element(ns + "RepeatOnNewPage")?.Value, "true", StringComparison.OrdinalIgnoreCase);
        return (
            designColumns,
            designRows,
            ReadGroups(element.Element(ns + "TablixRowHierarchy")?.Element(ns + "TablixMembers"), ns),
            ReadGroups(element.Element(ns + "TablixColumnHierarchy")?.Element(ns + "TablixMembers"), ns),
            repeat);
    }

    private static List<DesignGroup> ReadGroups(XElement? members, XNamespace ns)
    {
        var groups = new List<DesignGroup>();
        Walk(members);
        return groups;

        void Walk(XElement? node)
        {
            if (node is null)
            {
                return;
            }

            foreach (var member in node.Elements(ns + "TablixMember"))
            {
                var group = member.Element(ns + "Group");
                if (group?.Attribute("Name")?.Value is { Length: > 0 } name)
                {
                    var expression = group.Element(ns + "GroupExpressions")?.Elements(ns + "GroupExpression").FirstOrDefault()?.Value ?? "";
                    groups.Add(new DesignGroup(name, expression, expression.Length == 0));
                }

                Walk(member.Element(ns + "TablixMembers"));
            }
        }
    }

    /// <summary>
    /// Works out where an item's container starts in its band: the sum of the positions of the rectangles and
    /// tablix cells it is nested in. RDL stores Left/Top relative to the container, so without this nested
    /// items would all be drawn at the top-left of the body, over each other.
    /// </summary>
    private sealed class ContainerPlacement(RdlDocument document)
    {
        private readonly Dictionary<XElement, (double Left, double Top)> _origins = [];
        private readonly Dictionary<XElement, (DesignTablixGrid Grid, Dictionary<XElement, (int Row, int Column)> Cells)?> _tablixes = [];

        /// <summary>The offset of the item's container and the container's name.</summary>
        public (double Left, double Top, string? Container) Of(XElement item)
        {
            var container = item.Parent?.Parent;
            if (container is null || container.Name.LocalName is not ("Rectangle" or "CellContents"))
            {
                return (0, 0, null);
            }

            var (left, top) = Origin(item);
            var owner = container.Name.LocalName == "Rectangle" ? container : container.Ancestors(document.Ns + "Tablix").FirstOrDefault();
            return (left, top, owner?.Attribute("Name")?.Value);
        }

        /// <summary>Where the item's container starts in the band.</summary>
        private (double Left, double Top) Origin(XElement item)
        {
            var container = Holder(item);
            if (container is null)
            {
                return (0, 0);
            }

            if (_origins.TryGetValue(container, out var cached))
            {
                return cached;
            }

            var ns = document.Ns;
            (double, double) result = (0, 0);
            switch (container.Name.LocalName)
            {
                case "Rectangle":
                {
                    var (left, top) = Origin(container);
                    result = (left + Inches(container, "Left"), top + Inches(container, "Top"));
                    break;
                }

                case "CellContents" when container.Ancestors(ns + "Tablix").FirstOrDefault() is { } tablix:
                {
                    var (left, top) = Origin(tablix);
                    left += Inches(tablix, "Left");
                    top += Inches(tablix, "Top");
                    if (Grid(tablix) is { } grid && grid.Cells.TryGetValue(container, out var cell))
                    {
                        left += grid.Grid.ColumnWidths.Take(cell.Column).Sum();
                        top += grid.Grid.RowHeights.Take(cell.Row).Sum();
                    }

                    result = (left, top);
                    break;
                }
            }

            _origins[container] = result;
            return result;
        }

        private (DesignTablixGrid Grid, Dictionary<XElement, (int Row, int Column)> Cells)? Grid(XElement tablix)
        {
            if (_tablixes.TryGetValue(tablix, out var known))
            {
                return known;
            }

            (DesignTablixGrid, Dictionary<XElement, (int, int)>)? result = null;
            try
            {
                var name = tablix.Attribute("Name")?.Value;
                if (!string.IsNullOrEmpty(name))
                {
                    var structure = TablixStructure.Open(document, name);
                    if (ReferenceEquals(structure.Tablix, tablix))
                    {
                        var cells = new Dictionary<XElement, (int, int)>();
                        result = (structure.ReadGrid(cells), cells);
                    }
                }
            }
            catch (ReportDesignException)
            {
            }

            _tablixes[tablix] = result;
            return result;
        }

        /// <summary>The element that positions an item: the rectangle or cell contents around it, or the band.</summary>
        private static XElement? Holder(XElement item) =>
            item.Parent?.Name.LocalName == "ReportItems" ? item.Parent.Parent : item.Parent;

        private double Inches(XElement element, string name) =>
            Dimension.ToInches(element.Element(document.Ns + name)?.Value);
    }

    private static DesignItem WithStructure(DesignItem item, RdlDocument document, XElement element)
    {
        try
        {
            var structure = TablixStructure.Open(document, item.Name);
            if (!ReferenceEquals(structure.Tablix, element))
            {
                return item;
            }

            return item with
            {
                Grid = structure.ReadGrid(),
                RowHierarchy = structure.ReadHierarchy(row: true),
                ColumnHierarchy = structure.ReadHierarchy(row: false),
            };
        }
        catch (ReportDesignException)
        {
            return item;
        }
    }

    /// <summary>Table, Matrix (has a corner) or List (one cell holding a rectangle).</summary>
    private static string Variant(XElement element, XNamespace ns)
    {
        if (element.Name.LocalName != "Tablix")
        {
            return "";
        }

        if (element.Element(ns + "TablixCorner") is not null)
        {
            return "Matrix";
        }

        var cells = element.Element(ns + "TablixBody")?.Descendants(ns + "TablixCell").ToArray() ?? [];
        return cells.Length == 1 && cells[0].Element(ns + "CellContents")?.Element(ns + "Rectangle") is not null ? "List" : "Table";
    }

    /// <summary>Width of row headers, or height of column headers, of a matrix.</summary>
    private static double HeaderSizes(XElement? hierarchy, XNamespace ns)
    {
        var total = 0.0;
        var members = hierarchy?.Element(ns + "TablixMembers");
        while (members?.Elements(ns + "TablixMember").FirstOrDefault() is { } member)
        {
            total += Dimension.ToInches(member.Element(ns + "TablixHeader")?.Element(ns + "Size")?.Value);
            members = member.Element(ns + "TablixMembers");
        }

        return total;
    }

    private static string CellAt(DesignRow? row, int index) =>
        row is not null && index < row.Cells.Count ? row.Cells[index] : "";

    private static double Positive(double value, double fallback) => value > 0 ? value : fallback;
}
