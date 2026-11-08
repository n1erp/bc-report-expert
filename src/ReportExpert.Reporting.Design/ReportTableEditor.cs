using System.Xml.Linq;
using ReportExpert.Rdl.Core;

namespace ReportExpert.Reporting.Design;

internal static class ReportTableEditor
{
    public static void Scale(XElement tablix, XNamespace ns, double width, double height)
    {
        var columns = TablixNavigator.Columns(tablix, ns);
        var currentWidth = columns.Sum(column => Dimension.ToInches(column.Element(ns + "Width")?.Value));
        width = Math.Clamp(width, 0.4, 20);
        if (currentWidth > 0 && Math.Abs(currentWidth - width) > 0.02)
        {
            var factor = width / currentWidth;
            foreach (var column in columns)
            {
                var columnWidth = Math.Clamp(Dimension.ToInches(column.Element(ns + "Width")?.Value) * factor, 0.3, 8);
                Set(column, ns, "Width", Dimension.FromInches(columnWidth));
            }
        }

        TablixNavigator.RecalculateWidth(tablix, ns);
        var rows = TablixNavigator.Rows(tablix, ns);
        var currentHeight = rows.Sum(row => Dimension.ToInches(row.Element(ns + "Height")?.Value));
        height = Math.Clamp(height, 0.2, 20);
        if (currentHeight > 0 && Math.Abs(currentHeight - height) > 0.02)
        {
            var factor = height / currentHeight;
            foreach (var row in rows)
            {
                var rowHeight = Math.Clamp(Dimension.ToInches(row.Element(ns + "Height")?.Value) * factor, 0.18, 3);
                Set(row, ns, "Height", Dimension.FromInches(rowHeight));
            }
        }

        SyncHeight(tablix, ns);
    }

    public static void AddColumn(RdlDocument document, string table, string field)
    {
        var tablix = Require(document, table);
        var ns = document.Ns;
        RequireField(field);
        var columns = TablixNavigator.Columns(tablix, ns);
        if (columns.Count >= 16)
        {
            throw new ReportDesignException(ReportCodes.Limit);
        }

        var body = tablix.Element(ns + "TablixBody") ?? throw new ReportDesignException(ReportCodes.InvalidItem);
        var definitions = body.Element(ns + "TablixColumns") ?? throw new ReportDesignException(ReportCodes.InvalidItem);
        definitions.Add(new XElement(ns + "TablixColumn", new XElement(ns + "Width", "1.20in")));
        LeafColumns(tablix, ns).Add(new XElement(ns + "TablixMember"));

        var expression = FieldExpression(field);
        var rows = TablixNavigator.Rows(tablix, ns);
        var leaves = RowLeaves(tablix, ns);
        for (var index = 0; index < rows.Count; index++)
        {
            var role = index < leaves.Count ? Role(leaves[index], ns, index) : "Detail";
            var text = role switch
            {
                "Header" => field,
                "Detail" => expression,
                "Total" => "=CountRows()",
                _ => "",
            };
            Cells(rows[index], ns).Add(ReportLayoutEditor.Cell(document, ReportLayoutEditor.NextName(document, "Textbox"), text, role == "Header"));
        }

        TablixNavigator.RecalculateWidth(tablix, ns);
        ReportLayoutEditor.FitBody(document);
    }

    public static void EditColumn(RdlDocument document, string table, int index, string? header, string? value, double widthInches)
    {
        var tablix = Require(document, table);
        var ns = document.Ns;
        var columns = TablixNavigator.Columns(tablix, ns);
        if (index < 0 || index >= columns.Count)
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        Set(columns[index], ns, "Width", Dimension.FromInches(Math.Clamp(widthInches, 0.3, 8)));
        var rows = TablixNavigator.Rows(tablix, ns);
        var leaves = RowLeaves(tablix, ns);
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var role = rowIndex < leaves.Count ? Role(leaves[rowIndex], ns, rowIndex) : "";
            if (role == "Header" && header is not null)
            {
                SetCell(rows[rowIndex], ns, index, Trim(header, 80));
            }
            else if (role == "Detail" && value is not null)
            {
                SetCell(rows[rowIndex], ns, index, Trim(value, 500));
            }
        }

        TablixNavigator.RecalculateWidth(tablix, ns);
        ReportLayoutEditor.FitBody(document);
    }

    public static void MoveColumn(RdlDocument document, string table, int index, int direction)
    {
        var tablix = Require(document, table);
        var ns = document.Ns;
        var count = TablixNavigator.Columns(tablix, ns).Count;
        var target = index + (direction < 0 ? -1 : 1);
        if (index < 0 || index >= count || target < 0 || target >= count)
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        Reorder(tablix.Element(ns + "TablixBody")!.Element(ns + "TablixColumns")!, ns + "TablixColumn", index, target);
        Reorder(LeafColumns(tablix, ns), ns + "TablixMember", index, target);
        foreach (var row in TablixNavigator.Rows(tablix, ns))
        {
            Reorder(Cells(row, ns), ns + "TablixCell", index, target);
        }
    }

    public static void RemoveColumn(RdlDocument document, string table, int index)
    {
        var tablix = Require(document, table);
        var ns = document.Ns;
        var columns = TablixNavigator.Columns(tablix, ns);
        if (columns.Count <= 1 || index < 0 || index >= columns.Count)
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        columns[index].Remove();
        var members = LeafColumns(tablix, ns).Elements(ns + "TablixMember").ToList();
        if (index < members.Count)
        {
            members[index].Remove();
        }

        foreach (var row in TablixNavigator.Rows(tablix, ns))
        {
            var cells = TablixNavigator.Cells(row, ns);
            if (index < cells.Count)
            {
                cells[index].Remove();
            }
        }

        TablixNavigator.RecalculateWidth(tablix, ns);
        ReportLayoutEditor.FitBody(document);
    }

    public static void AddGroup(RdlDocument document, string table, string axis, string field)
    {
        var tablix = Require(document, table);
        var ns = document.Ns;
        RequireField(field);
        var expression = FieldExpression(field);
        if (string.Equals(axis, "Column", StringComparison.OrdinalIgnoreCase))
        {
            AddColumnGroup(document, tablix, expression, field);
            return;
        }

        AddRowGroup(document, tablix, expression, field);
    }

    public static void RemoveGroup(RdlDocument document, string table, string groupName)
    {
        var tablix = Require(document, table);
        var ns = document.Ns;
        var rowRoot = RowMembers(tablix, ns);
        var columnRoot = ColumnMembers(tablix, ns);
        var rowGroup = FindGroup(rowRoot, ns, groupName);
        if (rowGroup is not null)
        {
            if (rowGroup.Element(ns + "Group")?.Element(ns + "GroupExpressions") is null)
            {
                throw new ReportDesignException(ReportCodes.InvalidItem);
            }

            RemoveRowGroup(tablix, ns, rowRoot, rowGroup);
            SyncHeight(tablix, ns);
            ReportLayoutEditor.FitBody(document);
            return;
        }

        var columnGroup = FindGroup(columnRoot, ns, groupName) ?? throw new ReportDesignException(ReportCodes.InvalidItem);
        Unwrap(columnGroup);
    }

    public static void AddTotal(RdlDocument document, string table)
    {
        var tablix = Require(document, table);
        var ns = document.Ns;
        var root = RowMembers(tablix, ns);
        var details = FindDetails(root, ns) ?? throw new ReportDesignException(ReportCodes.InvalidItem);
        var parent = details.Parent!;
        var next = details.ElementsAfterSelf(ns + "TablixMember").FirstOrDefault();
        if (next is not null && next.Element(ns + "Group") is null && string.Equals(next.Element(ns + "KeepWithGroup")?.Value, "Before", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var leaves = LeafMembers(root, ns);
        var detailIndex = IndexOf(leaves, details);
        var count = TablixNavigator.Columns(tablix, ns).Count;
        var values = Enumerable.Range(0, count).Select(index => index == 0 ? "Total" : "=CountRows()").ToArray();
        InsertRow(document, tablix, detailIndex + 1, values, bold: true);
        details.AddAfterSelf(new XElement(ns + "TablixMember", new XElement(ns + "KeepWithGroup", "Before")));
        SyncHeight(tablix, ns);
        ReportLayoutEditor.FitBody(document);
    }

    public static void RepeatHeader(RdlDocument document, string table, bool repeat)
    {
        var tablix = Require(document, table);
        var ns = document.Ns;
        var first = RowMembers(tablix, ns).Elements(ns + "TablixMember").FirstOrDefault()
            ?? throw new ReportDesignException(ReportCodes.InvalidItem);
        Set(first, ns, "RepeatOnNewPage", repeat ? "true" : "false");
    }

    private static void AddRowGroup(RdlDocument document, XElement tablix, string expression, string field)
    {
        var ns = document.Ns;
        var root = RowMembers(tablix, ns);
        if (CountGroups(root, ns) >= 6 || HasExpression(root, ns, expression))
        {
            throw new ReportDesignException(ReportCodes.Limit);
        }

        var details = FindDetails(root, ns) ?? throw new ReportDesignException(ReportCodes.InvalidItem);
        var leaves = LeafMembers(root, ns);
        var detailIndex = IndexOf(leaves, details);
        var count = TablixNavigator.Columns(tablix, ns).Count;
        var values = Enumerable.Range(0, count).Select(index => index == 0 ? expression : "").ToArray();
        InsertRow(document, tablix, detailIndex, values, bold: true);

        var children = new XElement(ns + "TablixMembers");
        var wrapper = new XElement(
            ns + "TablixMember",
            Group(ns, ReportLayoutEditor.NextName(document, "Row" + field), expression),
            Sort(ns, expression),
            children);
        details.AddBeforeSelf(wrapper);
        children.Add(new XElement(ns + "TablixMember", new XElement(ns + "KeepWithGroup", "After")));
        details.Remove();
        children.Add(details);
        SyncHeight(tablix, ns);
        ReportLayoutEditor.FitBody(document);
    }

    private static void AddColumnGroup(RdlDocument document, XElement tablix, string expression, string field)
    {
        var ns = document.Ns;
        var root = ColumnMembers(tablix, ns);
        if (CountGroups(root, ns) >= 4 || HasExpression(root, ns, expression))
        {
            throw new ReportDesignException(ReportCodes.Limit);
        }

        var children = root.Elements(ns + "TablixMember").ToList();
        var nested = new XElement(ns + "TablixMembers");
        var wrapper = new XElement(
            ns + "TablixMember",
            Group(ns, ReportLayoutEditor.NextName(document, "Col" + field), expression),
            Sort(ns, expression),
            Header(document, expression),
            nested);
        foreach (var child in children)
        {
            child.Remove();
            nested.Add(child);
        }

        root.Add(wrapper);
    }

    private static void RemoveRowGroup(XElement tablix, XNamespace ns, XElement root, XElement groupMember)
    {
        var container = groupMember.Element(ns + "TablixMembers");
        var children = container?.Elements(ns + "TablixMember").ToList() ?? [];
        var staticRows = children.Where(member => member.Element(ns + "Group") is null && member.Element(ns + "TablixMembers") is null).ToList();
        var leaves = LeafMembers(root, ns);
        foreach (var index in staticRows.Select(member => IndexOf(leaves, member)).Where(index => index >= 0).OrderByDescending(index => index))
        {
            RemoveRow(tablix, ns, index);
        }

        foreach (var child in children.Where(member => !staticRows.Any(row => ReferenceEquals(row, member))).ToList())
        {
            child.Remove();
            groupMember.AddBeforeSelf(child);
        }

        groupMember.Remove();
    }

    private static void Unwrap(XElement groupMember)
    {
        var nested = groupMember.Element(groupMember.Name.Namespace + "TablixMembers");
        foreach (var child in nested?.Elements().ToList() ?? [])
        {
            child.Remove();
            groupMember.AddBeforeSelf(child);
        }

        groupMember.Remove();
    }

    private static void InsertRow(RdlDocument document, XElement tablix, int index, IReadOnlyList<string> values, bool bold)
    {
        var ns = document.Ns;
        var rows = tablix.Element(ns + "TablixBody")?.Element(ns + "TablixRows") ?? throw new ReportDesignException(ReportCodes.InvalidItem);
        var cells = new XElement(ns + "TablixCells");
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            cells.Add(ReportLayoutEditor.Cell(document, ReportLayoutEditor.NextName(document, "Textbox", reserved), value, bold));
        }

        var row = new XElement(ns + "TablixRow", new XElement(ns + "Height", "0.25in"), cells);
        var existing = rows.Elements(ns + "TablixRow").ToList();
        if (index < 0 || index >= existing.Count)
        {
            rows.Add(row);
        }
        else
        {
            existing[index].AddBeforeSelf(row);
        }
    }

    private static void RemoveRow(XElement tablix, XNamespace ns, int index)
    {
        var rows = TablixNavigator.Rows(tablix, ns);
        if (index >= 0 && index < rows.Count)
        {
            rows[index].Remove();
        }
    }

    private static XElement Header(RdlDocument document, string expression)
    {
        var ns = document.Ns;
        return new XElement(
            ns + "TablixHeader",
            new XElement(ns + "Size", "0.28in"),
            new XElement(
                ns + "CellContents",
                new XElement(
                    ns + "Textbox",
                    new XAttribute("Name", ReportLayoutEditor.NextName(document, "Textbox")),
                    new XElement(ns + "CanGrow", "true"),
                    ReportLayoutEditor.Paragraph(document, expression, bold: true),
                    new XElement(
                        ns + "Style",
                        new XElement(ns + "Border", new XElement(ns + "Style", "Solid")),
                        new XElement(ns + "PaddingLeft", "2pt"),
                        new XElement(ns + "PaddingRight", "2pt")))));
    }

    private static XElement Group(XNamespace ns, string name, string expression) =>
        new(
            ns + "Group",
            new XAttribute("Name", name),
            new XElement(ns + "GroupExpressions", new XElement(ns + "GroupExpression", expression)));

    private static XElement Sort(XNamespace ns, string expression) =>
        new(ns + "SortExpressions", new XElement(ns + "SortExpression", new XElement(ns + "Value", expression)));

    private static void SyncHeight(XElement tablix, XNamespace ns)
    {
        var total = TablixNavigator.Rows(tablix, ns).Sum(row => Dimension.ToInches(row.Element(ns + "Height")?.Value));
        Set(tablix, ns, "Height", Dimension.FromInches(Math.Max(0.25, total)));
    }

    private static void Reorder(XElement parent, XName name, int from, int to)
    {
        var items = parent.Elements(name).ToList();
        var moving = items[from];
        items.RemoveAt(from);
        items.Insert(to, moving);
        foreach (var item in parent.Elements(name).ToList())
        {
            item.Remove();
        }

        foreach (var item in items)
        {
            parent.Add(item);
        }
    }

    private static void SetCell(XElement row, XNamespace ns, int index, string text)
    {
        var cells = TablixNavigator.Cells(row, ns);
        if (index >= cells.Count)
        {
            return;
        }

        var value = cells[index].Descendants(ns + "Value").FirstOrDefault();
        if (value is not null)
        {
            value.Value = text;
        }
    }

    private static XElement Cells(XElement row, XNamespace ns) =>
        row.Element(ns + "TablixCells") ?? throw new ReportDesignException(ReportCodes.InvalidItem);

    private static XElement Require(RdlDocument document, string table)
    {
        try
        {
            return TablixNavigator.RequireTablix(document, table);
        }
        catch (RdlNotFoundException exception)
        {
            throw new ReportDesignException(ReportCodes.InvalidItem, exception);
        }
    }

    private static void RequireField(string field)
    {
        if (!ReportDefinitionGuard.ValidIdentifier(field))
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }
    }

    private static string FieldExpression(string field) => "=Fields!" + field + ".Value";

    private static string Trim(string value, int max) => value.Length > max ? value[..max] : value;

    private static XElement RowMembers(XElement tablix, XNamespace ns) =>
        tablix.Element(ns + "TablixRowHierarchy")?.Element(ns + "TablixMembers")
        ?? throw new ReportDesignException(ReportCodes.InvalidItem);

    private static XElement ColumnMembers(XElement tablix, XNamespace ns) =>
        tablix.Element(ns + "TablixColumnHierarchy")?.Element(ns + "TablixMembers")
        ?? throw new ReportDesignException(ReportCodes.InvalidItem);

    private static XElement LeafColumns(XElement tablix, XNamespace ns)
    {
        var members = ColumnMembers(tablix, ns);
        while (members.Elements(ns + "TablixMember").Count() == 1
            && members.Elements(ns + "TablixMember").First().Element(ns + "TablixMembers") is { } nested)
        {
            members = nested;
        }

        return members;
    }

    private static List<XElement> RowLeaves(XElement tablix, XNamespace ns) =>
        LeafMembers(tablix.Element(ns + "TablixRowHierarchy")?.Element(ns + "TablixMembers"), ns);

    internal static List<XElement> LeafMembers(XElement? members, XNamespace ns)
    {
        var leaves = new List<XElement>();
        Walk(members);
        return leaves;

        void Walk(XElement? node)
        {
            if (node is null)
            {
                return;
            }

            foreach (var member in node.Elements(ns + "TablixMember"))
            {
                var children = member.Element(ns + "TablixMembers");
                if (children is null)
                {
                    leaves.Add(member);
                }
                else
                {
                    Walk(children);
                }
            }
        }
    }

    internal static string Role(XElement member, XNamespace ns, int index)
    {
        var group = member.Element(ns + "Group");
        if (group is not null)
        {
            return group.Element(ns + "GroupExpressions") is null ? "Detail" : "Group";
        }

        if (string.Equals(member.Element(ns + "KeepWithGroup")?.Value, "Before", StringComparison.OrdinalIgnoreCase))
        {
            return "Total";
        }

        return index == 0 ? "Header" : "Group";
    }

    private static XElement? FindDetails(XElement members, XNamespace ns)
    {
        foreach (var member in members.Elements(ns + "TablixMember"))
        {
            var group = member.Element(ns + "Group");
            var nested = member.Element(ns + "TablixMembers");
            if (group is not null && group.Element(ns + "GroupExpressions") is null && nested is null)
            {
                return member;
            }

            if (nested is not null && FindDetails(nested, ns) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static XElement? FindGroup(XElement? members, XNamespace ns, string name)
    {
        if (members is null)
        {
            return null;
        }

        foreach (var member in members.Elements(ns + "TablixMember"))
        {
            if (string.Equals(member.Element(ns + "Group")?.Attribute("Name")?.Value, name, StringComparison.Ordinal))
            {
                return member;
            }

            if (FindGroup(member.Element(ns + "TablixMembers"), ns, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static bool HasExpression(XElement members, XNamespace ns, string expression) =>
        members.Descendants(ns + "GroupExpression").Any(node => string.Equals(node.Value, expression, StringComparison.OrdinalIgnoreCase));

    private static int CountGroups(XElement members, XNamespace ns) =>
        members.Descendants(ns + "Group").Count(group => group.Element(ns + "GroupExpressions") is not null);

    private static int IndexOf(IReadOnlyList<XElement> items, XElement target)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], target))
            {
                return index;
            }
        }

        return -1;
    }

    private static void Set(XElement parent, XNamespace ns, string name, string value)
    {
        var child = parent.Element(ns + name);
        if (child is null)
        {
            child = new XElement(ns + name);
            parent.Add(child);
        }

        child.Value = value;
    }
}
