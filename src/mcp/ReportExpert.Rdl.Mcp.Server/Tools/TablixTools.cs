using System.ComponentModel;
using ModelContextProtocol.Server;
using ReportExpert.Reporting.Design;

namespace ReportExpert.Rdl.Mcp.Server.Tools;

/// <summary>
/// Tablix structure tools: the grouping pane (parent, child and adjacent groups, totals, delete options,
/// group properties) and the tablix cell commands (insert or delete rows and columns inside or outside a
/// group, merge and split cells), driving the same engine as the REX designer.
/// </summary>
[McpServerToolType]
public static class TablixTools
{
    /// <summary>Returns the row and column hierarchies and the drawing grid of a table, list or matrix.</summary>
    /// <param name="filepath">Path to the report definition.</param>
    /// <param name="table">Data region name.</param>
    /// <returns>The hierarchies and grid.</returns>
    [McpServerTool(Name = "get_rdl_tablix_structure", ReadOnly = true, Idempotent = true)]
    [Description(
        "The structure of a table, list or matrix: the row and column hierarchies (groups, details and static " +
        "header/footer/total members, each with the body rows or columns it owns) and the cell grid with the " +
        "corner, row/column header cells and merged cells. Body indexes here are what the row, column and merge tools take.")]
    public static ToolResponse GetRdlTablixStructure(
        [Description("Absolute or relative path to the .rdl or .rdlc file.")] string filepath,
        [Description("Name of the table, list or matrix.")] string table) =>
        DesignSession.Read(filepath, (_, model) =>
        {
            var item = model.Items.FirstOrDefault(candidate => candidate.Kind == "Tablix" && candidate.Name == table)
                ?? throw new ReportDesignException(ReportCodes.InvalidItem);
            return Task.FromResult<object>(new
            {
                item.Name,
                item.DataSetName,
                item.Variant,
                item.Grid?.RowHeaderColumns,
                item.Grid?.ColumnHeaderRows,
                BodyRows = item.Grid?.BodyRowCount,
                BodyColumns = item.Grid?.BodyColumnCount,
                item.RowHierarchy,
                item.ColumnHierarchy,
                Cells = item.Grid?.Cells,
            });
        });

    /// <summary>Adds a group relative to another group or the details.</summary>
    /// <param name="filepath">Path to the report definition.</param>
    /// <param name="table">Data region name.</param>
    /// <param name="axis">Row or Column.</param>
    /// <param name="position">Parent, Child, AdjacentBefore or AdjacentAfter.</param>
    /// <param name="group_by">Field or expression.</param>
    /// <param name="target_group">Group to place relative to.</param>
    /// <param name="group_name">Name for the new group.</param>
    /// <param name="add_header">Add a group header row/column.</param>
    /// <param name="add_footer">Add a group footer row/column.</param>
    /// <param name="header_cell">Show the value in a header cell.</param>
    /// <param name="dry_run">Report the change without writing.</param>
    /// <returns>The edit outcome.</returns>
    [McpServerTool(Name = "insert_rdl_tablix_group", Destructive = true)]
    [Description(
        "Add a group to a table, list or matrix the way a grouping pane does: Parent (wraps the target), Child " +
        "(goes under the target group), AdjacentBefore or AdjacentAfter (a sibling with its own rows). Optionally " +
        "add a group header and/or footer row, and show the group value in a row-header column (default) or, " +
        "headerless, in the group header row.")]
    public static ToolResponse InsertRdlTablixGroup(
        [Description("Absolute or relative path to the .rdl or .rdlc file.")] string filepath,
        [Description("Name of the table, list or matrix.")] string table,
        [Description("Row or Column.")] string axis,
        [Description("Parent, Child, AdjacentBefore or AdjacentAfter.")] string position,
        [Description("Dataset field name, or an expression starting with '='. Empty adds a details group (adjacent only).")] string group_by,
        [Description("Existing group to place the new one relative to; omit for the details group.")] string? target_group = null,
        [Description("Name of the new group; generated from the field when omitted.")] string? group_name = null,
        [Description("Add a group header row (or column) before each instance.")] bool add_header = false,
        [Description("Add a group footer row (or column) after each instance.")] bool add_footer = false,
        [Description("Show the group value in its own row-header column (or column-header row).")] bool header_cell = true,
        [Description("When true, return the change as a diff and write nothing.")] bool dry_run = false) =>
        DesignSession.Mutate(filepath, dry_run, (service, model) =>
            service.InsertGroupAsync(model.Id, new TablixGroupInsert(table, axis, target_group, Parse<TablixGroupPosition>(position), group_by, group_name, add_header, add_footer, header_cell), CancellationToken.None),
            $"Added a {axis} group ({position}) to {table}");

    /// <summary>Deletes a group.</summary>
    /// <param name="filepath">Path to the report definition.</param>
    /// <param name="table">Data region name.</param>
    /// <param name="group">Group name.</param>
    /// <param name="mode">GroupOnly or GroupAndRelated.</param>
    /// <param name="dry_run">Report the change without writing.</param>
    /// <returns>The edit outcome.</returns>
    [McpServerTool(Name = "delete_rdl_tablix_group", Destructive = true)]
    [Description("Delete a tablix group. GroupOnly keeps its rows (they join the enclosing scope); GroupAndRelated also removes its own header/footer rows and its header column.")]
    public static ToolResponse DeleteRdlTablixGroup(
        [Description("Absolute or relative path to the .rdl or .rdlc file.")] string filepath,
        [Description("Name of the table, list or matrix.")] string table,
        [Description("Name of the group to delete.")] string group,
        [Description("GroupOnly or GroupAndRelated.")] string mode = "GroupOnly",
        [Description("When true, return the change as a diff and write nothing.")] bool dry_run = false) =>
        DesignSession.Mutate(filepath, dry_run, (service, model) =>
            service.DeleteGroupAsync(model.Id, table, group, Parse<TablixDeleteMode>(mode), CancellationToken.None),
            $"Deleted group {group} from {table}");

    /// <summary>Edits group properties.</summary>
    /// <param name="filepath">Path to the report definition.</param>
    /// <param name="table">Data region name.</param>
    /// <param name="group">Group name.</param>
    /// <param name="new_name">Rename the group.</param>
    /// <param name="group_expressions">Group expressions.</param>
    /// <param name="sort_expressions">Sort expressions.</param>
    /// <param name="filters">Group filters.</param>
    /// <param name="hidden">Initial visibility.</param>
    /// <param name="toggle_item">Toggle text box.</param>
    /// <param name="break_location">Page break location.</param>
    /// <param name="reset_page_number">Reset page numbers at each break.</param>
    /// <param name="page_name">Page name expression.</param>
    /// <param name="dry_run">Report the change without writing.</param>
    /// <returns>The edit outcome.</returns>
    [McpServerTool(Name = "edit_rdl_tablix_group", Destructive = true)]
    [Description(
        "Edit a group's properties: rename (scope references such as Sum(…, \"Group\") follow), group expressions, " +
        "sorting, filters, initial visibility and toggle item (drilldown), page breaks and page name. Omitted " +
        "arguments are left unchanged; an empty list clears.")]
    public static ToolResponse EditRdlTablixGroup(
        [Description("Absolute or relative path to the .rdl or .rdlc file.")] string filepath,
        [Description("Name of the table, list or matrix.")] string table,
        [Description("Name of the group.")] string group,
        [Description("New group name.")] string? new_name = null,
        [Description("Group expressions: field names or '=' expressions.")] string[]? group_expressions = null,
        [Description("Sort expressions, each 'expression' or 'expression|Descending'.")] string[]? sort_expressions = null,
        [Description("Filters, each 'expression|Operator|value1;value2' (operators: Equal, NotEqual, Like, GreaterThan, …, TopN, In, Between).")] string[]? filters = null,
        [Description("Initial visibility: 'true' to hide, '' to show, or an '=' expression.")] string? hidden = null,
        [Description("Text box that toggles the group (drilldown); '' to clear.")] string? toggle_item = null,
        [Description("None, Start, End, StartAndEnd or Between.")] string? break_location = null,
        [Description("Reset page numbers at each page break.")] bool? reset_page_number = null,
        [Description("Page name (an expression is allowed); '' to clear.")] string? page_name = null,
        [Description("When true, return the change as a diff and write nothing.")] bool dry_run = false) =>
        DesignSession.Mutate(filepath, dry_run, (service, model) =>
            service.EditGroupAsync(model.Id, new TablixGroupEdit(
                table,
                group,
                new_name,
                group_expressions,
                sort_expressions?.Select(ParseSort).ToArray(),
                filters?.Select(ParseFilter).ToArray(),
                hidden,
                toggle_item,
                break_location,
                reset_page_number,
                page_name), CancellationToken.None),
            $"Edited group {group} in {table}");

    /// <summary>Adds a total before or after a group.</summary>
    /// <param name="filepath">Path to the report definition.</param>
    /// <param name="table">Data region name.</param>
    /// <param name="axis">Row or Column.</param>
    /// <param name="group">Group, or omitted for the details.</param>
    /// <param name="after">After (true) or before (false).</param>
    /// <param name="dry_run">Report the change without writing.</param>
    /// <returns>The edit outcome.</returns>
    [McpServerTool(Name = "add_rdl_tablix_total", Destructive = true)]
    [Description("Add a total row (or column) before or after a group, outside it: numeric fields are summed with Sum(), the first other cell reads Total.")]
    public static ToolResponse AddRdlTablixTotal(
        [Description("Absolute or relative path to the .rdl or .rdlc file.")] string filepath,
        [Description("Name of the table, list or matrix.")] string table,
        [Description("Row or Column.")] string axis = "Row",
        [Description("Group to total; omit for the details group.")] string? group = null,
        [Description("True to add the total after the group, false before it.")] bool after = true,
        [Description("When true, return the change as a diff and write nothing.")] bool dry_run = false) =>
        DesignSession.Mutate(filepath, dry_run, (service, model) =>
            service.AddGroupTotalAsync(model.Id, table, axis, group, after, CancellationToken.None),
            $"Added a total to {table}");

    /// <summary>Inserts a body row or column.</summary>
    /// <param name="filepath">Path to the report definition.</param>
    /// <param name="table">Data region name.</param>
    /// <param name="axis">Row or Column.</param>
    /// <param name="index">Body index.</param>
    /// <param name="placement">Where to insert.</param>
    /// <param name="dry_run">Report the change without writing.</param>
    /// <returns>The edit outcome.</returns>
    [McpServerTool(Name = "insert_rdl_tablix_row_or_column", Destructive = true)]
    [Description(
        "Insert a body row or column next to body index 'index': InsideGroupBefore/InsideGroupAfter repeat with " +
        "the innermost group of that row (for example a second details row), OutsideGroupBefore/OutsideGroupAfter " +
        "go outside it, kept with the group (for example a group footer or a grand total).")]
    public static ToolResponse InsertRdlTablixRowOrColumn(
        [Description("Absolute or relative path to the .rdl or .rdlc file.")] string filepath,
        [Description("Name of the table, list or matrix.")] string table,
        [Description("Row or Column.")] string axis,
        [Description("Zero-based body row (or column) index, as get_rdl_tablix_structure reports it.")] int index,
        [Description("InsideGroupBefore, InsideGroupAfter, OutsideGroupBefore or OutsideGroupAfter.")] string placement = "InsideGroupAfter",
        [Description("When true, return the change as a diff and write nothing.")] bool dry_run = false) =>
        DesignSession.Mutate(filepath, dry_run, (service, model) =>
            service.InsertTablixSlotAsync(model.Id, table, axis, index, Parse<TablixInsertPlacement>(placement), CancellationToken.None),
            $"Inserted a {axis.ToLowerInvariant()} in {table}");

    /// <summary>Deletes a body row or column.</summary>
    /// <param name="filepath">Path to the report definition.</param>
    /// <param name="table">Data region name.</param>
    /// <param name="axis">Row or Column.</param>
    /// <param name="index">Body index.</param>
    /// <param name="mode">GroupOnly or GroupAndRelated.</param>
    /// <param name="dry_run">Report the change without writing.</param>
    /// <returns>The edit outcome.</returns>
    [McpServerTool(Name = "delete_rdl_tablix_row_or_column", Destructive = true)]
    [Description("Delete a body row or column. GroupOnly deletes just that row (column); GroupAndRelated deletes the innermost group it belongs to, with all of that group's rows (columns).")]
    public static ToolResponse DeleteRdlTablixRowOrColumn(
        [Description("Absolute or relative path to the .rdl or .rdlc file.")] string filepath,
        [Description("Name of the table, list or matrix.")] string table,
        [Description("Row or Column.")] string axis,
        [Description("Zero-based body row (or column) index.")] int index,
        [Description("GroupOnly (rows only) or GroupAndRelated (rows and associated groups).")] string mode = "GroupOnly",
        [Description("When true, return the change as a diff and write nothing.")] bool dry_run = false) =>
        DesignSession.Mutate(filepath, dry_run, (service, model) =>
            service.DeleteTablixSlotAsync(model.Id, table, axis, index, Parse<TablixDeleteMode>(mode), CancellationToken.None),
            $"Deleted a {axis.ToLowerInvariant()} from {table}");

    /// <summary>Merges or splits body cells.</summary>
    /// <param name="filepath">Path to the report definition.</param>
    /// <param name="table">Data region name.</param>
    /// <param name="row">Body row.</param>
    /// <param name="first_column">First body column.</param>
    /// <param name="last_column">Last body column; equal to first_column to split.</param>
    /// <param name="dry_run">Report the change without writing.</param>
    /// <returns>The edit outcome.</returns>
    [McpServerTool(Name = "merge_rdl_tablix_cells", Destructive = true)]
    [Description("Merge body cells first_column..last_column of a body row into one cell (they must be static sibling columns), or split a merged cell when last_column equals first_column.")]
    public static ToolResponse MergeRdlTablixCells(
        [Description("Absolute or relative path to the .rdl or .rdlc file.")] string filepath,
        [Description("Name of the table, list or matrix.")] string table,
        [Description("Zero-based body row index.")] int row,
        [Description("Zero-based first body column.")] int first_column,
        [Description("Zero-based last body column; the same as first_column splits the cell.")] int last_column,
        [Description("When true, return the change as a diff and write nothing.")] bool dry_run = false) =>
        DesignSession.Mutate(filepath, dry_run, (service, model) =>
            last_column == first_column
                ? service.SplitTablixCellAsync(model.Id, table, row, first_column, CancellationToken.None)
                : service.MergeTablixCellsAsync(model.Id, table, row, first_column, last_column, CancellationToken.None),
            last_column == first_column ? $"Split a cell in {table}" : $"Merged cells in {table}");

    private static T Parse<T>(string value)
        where T : struct, Enum =>
        Enum.TryParse<T>(value?.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ReportDesignException(ReportCodes.InvalidValue);

    private static DesignSort ParseSort(string value)
    {
        var parts = value.Split('|');
        return new DesignSort(parts[0].Trim(), parts.Length > 1 && parts[1].Trim().Equals("Descending", StringComparison.OrdinalIgnoreCase) ? "Descending" : "Ascending");
    }

    private static DesignGroupFilter ParseFilter(string value)
    {
        var parts = value.Split('|');
        return new DesignGroupFilter(
            parts[0].Trim(),
            parts.Length > 1 ? parts[1].Trim() : "Equal",
            parts.Length > 2 ? parts[2].Split(';', StringSplitOptions.TrimEntries) : [""]);
    }
}
