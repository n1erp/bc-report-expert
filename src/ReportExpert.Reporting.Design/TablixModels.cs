namespace ReportExpert.Reporting.Design;

/// <summary>Where a new tablix group goes relative to the group (or details) it is added from.</summary>
/// <remarks>
/// These are the four placements of a tablix grouping editor: a parent wraps the target, a child goes
/// under it, and an adjacent group becomes a sibling before or after it.
/// </remarks>
public enum TablixGroupPosition
{
    Parent,
    Child,
    AdjacentBefore,
    AdjacentAfter,
}

/// <summary>How a group is removed from a tablix hierarchy.</summary>
public enum TablixDeleteMode
{
    /// <summary>Remove the group only: its rows (or columns) stay and become part of the enclosing scope.</summary>
    GroupOnly,

    /// <summary>Remove the group together with its own header and footer rows (or columns) and its header cells.</summary>
    GroupAndRelated,
}

/// <summary>Where a new tablix row (or column) goes relative to a body cell.</summary>
public enum TablixInsertPlacement
{
    /// <summary>Above (or left of) the cell, repeated with the innermost group the cell is in.</summary>
    InsideGroupBefore,

    /// <summary>Below (or right of) the cell, repeated with the innermost group the cell is in.</summary>
    InsideGroupAfter,

    /// <summary>Above (or left of) the innermost group the cell is in, once per instance of the enclosing scope.</summary>
    OutsideGroupBefore,

    /// <summary>Below (or right of) the innermost group the cell is in, once per instance of the enclosing scope.</summary>
    OutsideGroupAfter,
}

/// <summary>A request to add a group to a tablix.</summary>
/// <param name="Table">The tablix name.</param>
/// <param name="Axis">"Row" or "Column".</param>
/// <param name="Target">
/// The group the new group is placed relative to. Null or empty targets the details group, or the
/// innermost group when the hierarchy has no details group.
/// </param>
/// <param name="Position">Parent, child, or adjacent before/after.</param>
/// <param name="GroupBy">A field name or an expression starting with <c>=</c>. Empty creates a details group (adjacent only).</param>
/// <param name="Name">The group name; generated from the field when empty.</param>
/// <param name="AddHeader">Add a header row (or column) repeated before each group instance.</param>
/// <param name="AddFooter">Add a footer row (or column) repeated after each group instance.</param>
/// <param name="HeaderCell">
/// Show the group value in a row-header column (or column-header row), the way a grouped table does.
/// When false the group is headerless and its value goes in the first cell of the group header row.
/// </param>
public sealed record TablixGroupInsert(
    string Table,
    string Axis,
    string? Target,
    TablixGroupPosition Position,
    string GroupBy,
    string? Name = null,
    bool AddHeader = false,
    bool AddFooter = false,
    bool HeaderCell = true);

/// <summary>A sort expression of a group or data region.</summary>
public sealed record DesignSort(string Expression, string Direction);

/// <summary>A filter of a group or data region.</summary>
public sealed record DesignGroupFilter(string Expression, string Operator, IReadOnlyList<string> Values);

/// <summary>A group variable.</summary>
public sealed record DesignVariable(string Name, string Value);

/// <summary>
/// The full definition of a tablix group, as shown by the group properties dialog
/// (general, sorting, filters, visibility, page breaks and variables).
/// </summary>
public sealed record DesignGroupDetail(
    string Name,
    string Axis,
    IReadOnlyList<string> GroupExpressions,
    IReadOnlyList<DesignSort> Sorts,
    IReadOnlyList<DesignGroupFilter> Filters,
    string Hidden,
    string ToggleItem,
    string BreakLocation,
    bool ResetPageNumber,
    string PageName,
    IReadOnlyList<DesignVariable> Variables,
    string? ParentGroup,
    bool IsDetails)
{
    /// <summary>
    /// Text boxes that may toggle this group's visibility (drilldown): those in the header cells of the
    /// groups that contain it, and those outside the data region.
    /// </summary>
    public IReadOnlyList<string> ToggleCandidates { get; init; } = [];
}

/// <summary>An edit of a tablix group. Null members are left unchanged.</summary>
public sealed record TablixGroupEdit(
    string Table,
    string Group,
    string? NewName = null,
    IReadOnlyList<string>? GroupExpressions = null,
    IReadOnlyList<DesignSort>? Sorts = null,
    IReadOnlyList<DesignGroupFilter>? Filters = null,
    string? Hidden = null,
    string? ToggleItem = null,
    string? BreakLocation = null,
    bool? ResetPageNumber = null,
    string? PageName = null,
    IReadOnlyList<DesignVariable>? Variables = null);

/// <summary>
/// An edit of a static tablix member (a header, footer or total row or column) located by the body row
/// or column it owns. Null members are left unchanged.
/// </summary>
public sealed record TablixMemberEdit(
    string Table,
    string Axis,
    int Index,
    string? KeepWithGroup = null,
    bool? RepeatOnNewPage = null,
    bool? KeepTogether = null,
    string? Hidden = null,
    string? ToggleItem = null,
    bool? HideIfNoRows = null);

/// <summary>
/// One node of a tablix row or column hierarchy, as the grouping pane shows it. Dynamic nodes are groups
/// (details included); static nodes are header, footer, total or plain rows/columns.
/// </summary>
public sealed record DesignMember(
    string Kind,
    string Name,
    string Expression,
    int Depth,
    int FirstIndex,
    int LastIndex,
    bool HasHeader,
    string KeepWithGroup,
    bool RepeatOnNewPage,
    bool Hidden,
    IReadOnlyList<DesignMember> Children)
{
    public bool IsDynamic => Kind is "Group" or "Details";

    public string Label => Kind switch
    {
        "Details" => string.IsNullOrEmpty(Name) ? "(Details)" : "(" + Name + ")",
        "Group" => Name,
        _ => "(Static)",
    };
}

/// <summary>Which part of a tablix a grid cell belongs to.</summary>
public static class TablixArea
{
    public const string Corner = "Corner";
    public const string RowHeader = "RowHeader";
    public const string ColumnHeader = "ColumnHeader";
    public const string Body = "Body";
}

/// <summary>
/// One visible cell of a tablix as the designer draws it: corner, row header, column header or body,
/// with its span. <see cref="BodyRow"/>/<see cref="BodyColumn"/> locate body cells for cell commands.
/// </summary>
public sealed record DesignGridCell(
    int Row,
    int Column,
    int RowSpan,
    int ColSpan,
    string Area,
    string TextboxName,
    string Value,
    int BodyRow = -1,
    int BodyColumn = -1,
    string Group = "")
{
    /// <summary>The font size of the cell's text box, such as "7pt"; empty when it has none.</summary>
    public string FontSize { get; init; } = "";

    /// <summary>The formatting the designer draws for the cell (font, colours, alignment).</summary>
    public DesignTextStyle Style { get; init; } = DesignTextStyle.Empty;
}

/// <summary>
/// The full drawing grid of a tablix: row-header columns first, then body columns; column-header rows
/// first, then body rows.
/// </summary>
public sealed record DesignTablixGrid(
    int RowHeaderColumns,
    int ColumnHeaderRows,
    IReadOnlyList<double> ColumnWidths,
    IReadOnlyList<double> RowHeights,
    IReadOnlyList<DesignGridCell> Cells)
{
    public int BodyColumnCount => ColumnWidths.Count - RowHeaderColumns;

    public int BodyRowCount => RowHeights.Count - ColumnHeaderRows;
}

/// <summary>Which arrange operations (align, size, centre, grid, order, spacing) apply to a selection.</summary>
public static class ReportArrangeOperations
{
    /// <summary>The operations that apply to <paramref name="count"/> selected surface items.</summary>
    public static IReadOnlyList<string> For(int count) => ReportItemOperations.Applicable(count);
}
