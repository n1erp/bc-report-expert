using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReportExpert.Reporting.Design;

namespace ReportExpert.App.Reports;

/// <summary>One line of the grouping pane: a group, the details, or (in advanced mode) a static member.</summary>
public sealed class GroupingNode
{
    public GroupingNode(DesignMember member, string axis, IReadOnlyList<GroupingNode> children)
    {
        Member = member;
        Axis = axis;
        Children = children;
    }

    public DesignMember Member { get; }

    public string Axis { get; }

    public IReadOnlyList<GroupingNode> Children { get; }

    public string Label => Member.Label;

    public bool IsGroup => Member.IsDynamic;

    public bool IsStatic => !Member.IsDynamic;

    public string Icon => Member.Kind switch
    {
        "Details" => "TextBulletListLtr24",
        "Group" => "Grid24",
        _ => "RectangleLandscape24",
    };

    public string ToolTip => Member.Kind switch
    {
        "Group" => $"{Member.Name}: grouped on {Member.Expression}",
        "Details" => "Details: one instance per row of data",
        _ => string.IsNullOrEmpty(Member.KeepWithGroup) ? "Static member" : $"Static member, kept with the group {(Member.KeepWithGroup == "After" ? "after" : "before")} it",
    };
}

/// <summary>
/// The tablix part of the designer: the Report Builder-style grouping pane (row and column groups, with the
/// static members in advanced mode) and the tablix cell commands (insert row/column inside or outside a group,
/// delete rows/columns, merge/split, add group, add total, group properties).
/// </summary>
public sealed partial class ReportCenterViewModel
{
    public ObservableCollection<GroupingNode> RowGroupNodes { get; } = [];

    public ObservableCollection<GroupingNode> ColumnGroupNodes { get; } = [];

    /// <summary>Show static members (header, footer and total rows) in the grouping pane.</summary>
    [ObservableProperty]
    private bool _showStaticMembers;

    partial void OnShowStaticMembersChanged(bool value) => RefreshGrouping();

    /// <summary>The tablix the grouping pane and the cell commands work on: the selected tablix, or the one holding the selected cell.</summary>
    public DesignItem? SelectedTablix =>
        Current?.Items.FirstOrDefault(item => item.Kind == "Tablix"
            && (item.Name == Current.SelectedItem || (item.Grid?.Cells.Any(cell => cell.TextboxName.Length > 0 && cell.TextboxName == Current.SelectedItem) ?? false)));

    /// <summary>The selected tablix cell, when a cell of a tablix is selected.</summary>
    public DesignGridCell? SelectedCell =>
        SelectedTablix?.Grid?.Cells.FirstOrDefault(cell => cell.TextboxName.Length > 0 && cell.TextboxName == Current?.SelectedItem);

    public bool HasTablixTarget => SelectedTablix is not null;

    private void RefreshGrouping()
    {
        var table = SelectedTablix;
        RowGroupNodes.Clear();
        ColumnGroupNodes.Clear();
        foreach (var node in Nodes(table?.RowHierarchy, "Row"))
        {
            RowGroupNodes.Add(node);
        }

        foreach (var node in Nodes(table?.ColumnHierarchy, "Column"))
        {
            ColumnGroupNodes.Add(node);
        }

        OnPropertyChanged(nameof(SelectedTablix));
        OnPropertyChanged(nameof(SelectedCell));
        OnPropertyChanged(nameof(HasTablixTarget));
    }

    /// <summary>
    /// Builds the pane lines. Without advanced mode only groups are listed and static members are
    /// skipped (their dynamic descendants are lifted), the way a grouping pane shows a table.
    /// </summary>
    private IReadOnlyList<GroupingNode> Nodes(IReadOnlyList<DesignMember>? members, string axis)
    {
        if (members is null)
        {
            return [];
        }

        var nodes = new List<GroupingNode>();
        foreach (var member in members)
        {
            var children = Nodes(member.Children, axis);
            if (member.IsDynamic || ShowStaticMembers)
            {
                nodes.Add(new GroupingNode(member, axis, children));
            }
            else
            {
                nodes.AddRange(children);
            }
        }

        return nodes;
    }

    private IReadOnlyList<string> TablixFields(DesignItem table) =>
        (Current?.DataSets.FirstOrDefault(dataSet => dataSet.Name == table.DataSetName) ?? Current?.DataSets.FirstOrDefault())?.Fields.Select(field => field.Name).ToArray() ?? [];

    /// <summary>The innermost group (or details) whose rows (or columns) contain a body index.</summary>
    private static DesignMember? InnermostGroup(IReadOnlyList<DesignMember>? members, int index)
    {
        DesignMember? found = null;
        Walk(members);
        return found;

        void Walk(IReadOnlyList<DesignMember>? list)
        {
            foreach (var member in list ?? [])
            {
                if (index < member.FirstIndex || index > member.LastIndex)
                {
                    continue;
                }

                if (member.IsDynamic)
                {
                    found = member;
                }

                Walk(member.Children);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Grouping pane commands (the parameter is the pane line, or null for the cell's innermost group)
    // ---------------------------------------------------------------------------------------------

    [RelayCommand]
    private Task AddParentGroupAsync(GroupingNode? node) => AddGroupFromAsync(node, TablixGroupPosition.Parent);

    [RelayCommand]
    private Task AddChildGroupAsync(GroupingNode? node) => AddGroupFromAsync(node, TablixGroupPosition.Child);

    [RelayCommand]
    private Task AddAdjacentBeforeAsync(GroupingNode? node) => AddGroupFromAsync(node, TablixGroupPosition.AdjacentBefore);

    [RelayCommand]
    private Task AddAdjacentAfterAsync(GroupingNode? node) => AddGroupFromAsync(node, TablixGroupPosition.AdjacentAfter);

    [RelayCommand]
    private Task AddTotalBeforeAsync(GroupingNode? node) => AddTotalFromAsync(node, after: false);

    [RelayCommand]
    private Task AddTotalAfterAsync(GroupingNode? node) => AddTotalFromAsync(node, after: true);

    /// <summary>Adds a row group (Parent, Child, AdjacentBefore, AdjacentAfter) from the selected cell.</summary>
    [RelayCommand]
    private Task AddRowGroupAtCellAsync(string? position) => AddGroupAtCellAsync("Row", position);

    [RelayCommand]
    private Task AddColumnGroupAtCellAsync(string? position) => AddGroupAtCellAsync("Column", position);

    private Task AddGroupAtCellAsync(string axis, string? position)
    {
        if (!Enum.TryParse<TablixGroupPosition>(position, out var where))
        {
            where = TablixGroupPosition.Parent;
        }

        var target = CellGroup(axis);
        return AddGroupCoreAsync(axis, target?.Kind == "Group" ? target.Name : null, where);
    }

    private DesignMember? CellGroup(string axis)
    {
        var table = SelectedTablix;
        var cell = SelectedCell;
        if (table is null)
        {
            return null;
        }

        var index = axis == "Row" ? cell?.BodyRow ?? -1 : cell?.BodyColumn ?? -1;
        return index < 0 ? null : InnermostGroup(axis == "Row" ? table.RowHierarchy : table.ColumnHierarchy, index);
    }

    private Task AddGroupFromAsync(GroupingNode? node, TablixGroupPosition position) =>
        AddGroupCoreAsync(node?.Axis ?? "Row", node?.Member.Kind == "Group" ? node.Member.Name : null, position);

    private Task AddGroupCoreAsync(string axis, string? target, TablixGroupPosition position) => WithReportAsync(async report =>
    {
        if (SelectedTablix is not { } table)
        {
            Fail("Select a table, matrix or list first.");
            return;
        }

        var title = position switch
        {
            TablixGroupPosition.Parent => axis + " group: add parent group",
            TablixGroupPosition.Child => axis + " group: add child group",
            TablixGroupPosition.AdjacentBefore => axis + (axis == "Row" ? " group: add adjacent group above" : " group: add adjacent group left"),
            _ => axis + (axis == "Row" ? " group: add adjacent group below" : " group: add adjacent group right"),
        };
        var adjacent = position is TablixGroupPosition.AdjacentBefore or TablixGroupPosition.AdjacentAfter;
        var choice = AddGroupDialog.Ask(_owner(), title, TablixFields(table), string.IsNullOrWhiteSpace(GroupField) ? SelectedField?.Name : GroupField, axis == "Row", allowDetails: adjacent);
        if (choice is null)
        {
            return;
        }

        Show(await _service.InsertGroupAsync(report.Id, new TablixGroupInsert(table.Name, axis, target, position, choice.GroupBy, choice.Name, choice.Header, choice.Footer, choice.HeaderCell), CancellationToken.None));
        Succeed($"Added a {axis.ToLowerInvariant()} group to {table.Name}.");
    });

    private Task AddTotalFromAsync(GroupingNode? node, bool after) => WithReportAsync(async report =>
    {
        if (SelectedTablix is not { } table)
        {
            return;
        }

        var target = node?.Member.Kind == "Group" ? node.Member.Name : null;
        Show(await _service.AddGroupTotalAsync(report.Id, table.Name, node?.Axis ?? "Row", target, after, CancellationToken.None));
        Succeed("Total added.");
    });

    /// <summary>Adds a total row ("Row") or column ("Column") after the innermost group of the selected cell.</summary>
    [RelayCommand]
    private Task AddTotalAtCellAsync(string? axis) => WithReportAsync(async report =>
    {
        if (SelectedTablix is not { } table)
        {
            return;
        }

        var group = CellGroup(axis ?? "Row");
        Show(await _service.AddGroupTotalAsync(report.Id, table.Name, axis ?? "Row", group?.Kind == "Group" ? group.Name : null, after: true, CancellationToken.None));
        Succeed("Total added.");
    });

    [RelayCommand]
    private Task DeleteGroupNodeAsync(GroupingNode? node) => WithReportAsync(async report =>
    {
        if (node is null || !node.IsGroup || SelectedTablix is not { } table)
        {
            return;
        }

        var choice = ChoiceDialog.Ask(_owner(), "Delete Group", "Delete group options:", "Delete group only", node.Axis == "Row" ? "Delete group and related rows and columns" : "Delete group and related columns and rows");
        if (choice is null)
        {
            return;
        }

        Show(await _service.DeleteGroupAsync(report.Id, table.Name, node.Member.Name, choice == 0 ? TablixDeleteMode.GroupOnly : TablixDeleteMode.GroupAndRelated, CancellationToken.None));
        Succeed($"Deleted group {node.Member.Name}.");
    });

    [RelayCommand]
    private Task GroupPropertiesAsync(GroupingNode? node) => WithReportAsync(async report =>
    {
        if (SelectedTablix is not { } table)
        {
            return;
        }

        var name = node?.IsGroup == true ? node.Member.Name : CellGroup("Row")?.Name;
        if (string.IsNullOrEmpty(name))
        {
            Fail("Select a group in the grouping pane.");
            return;
        }

        var detail = await _service.ReadGroupAsync(report.Id, table.Name, name, CancellationToken.None);
        var edit = GroupPropertiesDialog.Ask(_owner(), table.Name, detail, TablixFields(table));
        if (edit is not null)
        {
            Show(await _service.EditGroupAsync(report.Id, edit, CancellationToken.None));
            Succeed($"Updated group {edit.NewName ?? name}.");
        }
    });

    /// <summary>Shows or hides a static row (or column): Row Visibility / Column Visibility on a pane line.</summary>
    [RelayCommand]
    private Task ToggleMemberHiddenAsync(GroupingNode? node) => WithReportAsync(async report =>
    {
        if (node is null || SelectedTablix is not { } table)
        {
            return;
        }

        if (node.IsGroup)
        {
            Show(await _service.EditGroupAsync(report.Id, new TablixGroupEdit(table.Name, node.Member.Name, Hidden: node.Member.Hidden ? "" : "true"), CancellationToken.None));
            return;
        }

        Show(await _service.EditTablixMemberAsync(report.Id, new TablixMemberEdit(table.Name, node.Axis, node.Member.FirstIndex, Hidden: node.Member.Hidden ? "" : "true"), CancellationToken.None));
    });

    [RelayCommand]
    private Task ToggleRepeatOnNewPageAsync(GroupingNode? node) => WithReportAsync(async report =>
    {
        if (node is null || node.IsGroup || SelectedTablix is not { } table)
        {
            return;
        }

        Show(await _service.EditTablixMemberAsync(report.Id, new TablixMemberEdit(table.Name, node.Axis, node.Member.FirstIndex, RepeatOnNewPage: !node.Member.RepeatOnNewPage), CancellationToken.None));
    });

    // ---------------------------------------------------------------------------------------------
    // Tablix cell commands
    // ---------------------------------------------------------------------------------------------

    /// <summary>Insert Row / Insert Column: the parameter is "Row:InsideGroupBefore", "Column:OutsideGroupAfter", ….</summary>
    [RelayCommand]
    private Task InsertAtCellAsync(string? spec) => WithReportAsync(async report =>
    {
        if (SelectedTablix is not { } table || SelectedCell is not { } cell || spec?.Split(':') is not [var axis, var where] || !Enum.TryParse<TablixInsertPlacement>(where, out var placement))
        {
            Fail("Select a cell of a table first.");
            return;
        }

        var index = axis == "Row" ? cell.BodyRow : cell.BodyColumn;
        if (index < 0)
        {
            Fail("Select a body cell (not a header cell).");
            return;
        }

        Show(await _service.InsertTablixSlotAsync(report.Id, table.Name, axis, index, placement, CancellationToken.None));
    });

    /// <summary>Delete Rows ("Row") or Delete Columns ("Column") at the selected cell, asking about groups when it belongs to one.</summary>
    [RelayCommand]
    private Task DeleteAtCellAsync(string? axis) => WithReportAsync(async report =>
    {
        axis ??= "Row";
        if (SelectedTablix is not { } table || SelectedCell is not { } cell)
        {
            Fail("Select a cell of a table first.");
            return;
        }

        var index = axis == "Row" ? cell.BodyRow : cell.BodyColumn;
        if (index < 0)
        {
            return;
        }

        var mode = TablixDeleteMode.GroupOnly;
        if (CellGroup(axis) is { Kind: "Group" })
        {
            var noun = axis == "Row" ? "rows" : "columns";
            var choice = ChoiceDialog.Ask(_owner(), "Delete " + (axis == "Row" ? "Rows" : "Columns"), $"Delete {noun[..^1]} options:", $"Delete {noun} only", $"Delete {noun} and associated groups");
            if (choice is null)
            {
                return;
            }

            mode = choice == 0 ? TablixDeleteMode.GroupOnly : TablixDeleteMode.GroupAndRelated;
        }

        Show(await _service.DeleteTablixSlotAsync(report.Id, table.Name, axis, index, mode, CancellationToken.None));
    });

    /// <summary>Merges the selected body cell with the cell to its right.</summary>
    [RelayCommand]
    private Task MergeRightAsync() => WithReportAsync(async report =>
    {
        if (SelectedTablix is not { } table || SelectedCell is not { BodyRow: >= 0 } cell)
        {
            Fail("Select a body cell first.");
            return;
        }

        Show(await _service.MergeTablixCellsAsync(report.Id, table.Name, cell.BodyRow, cell.BodyColumn, cell.BodyColumn + cell.ColSpan, CancellationToken.None));
    });

    [RelayCommand]
    private Task SplitCellAsync() => WithReportAsync(async report =>
    {
        if (SelectedTablix is not { } table || SelectedCell is not { BodyRow: >= 0 } cell)
        {
            return;
        }

        Show(await _service.SplitTablixCellAsync(report.Id, table.Name, cell.BodyRow, cell.BodyColumn, CancellationToken.None));
    });
}
