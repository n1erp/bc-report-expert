using System.Globalization;
using System.Xml.Linq;
using ReportExpert.Rdl.Core;

namespace ReportExpert.Reporting.Design;

/// <summary>
/// Structural editing of a tablix: the row and column hierarchies, the body rows and columns they own,
/// the header cells of row and column groups, and the corner. This is REX's own implementation of the
/// grouping model a tablix designer exposes (parent, child and adjacent groups; group header and footer
/// rows; totals; insert row/column inside or outside a group; delete group only or with its rows; merge
/// and split cells).
/// </summary>
/// <remarks>
/// <para>
/// The hierarchies are trees of <c>TablixMember</c>. Every leaf member owns one body row (row hierarchy)
/// or one body column (column hierarchy), in document order. A member that carries a <c>TablixHeader</c>
/// owns a header cell spanning all the body rows (or columns) of its subtree; every root-to-leaf path of a
/// hierarchy must carry the same number of headers, and that number is the count of row-header columns
/// (or column-header rows). The corner has one cell per row-header column and column-header row.
/// </para>
/// <para>
/// Every operation keeps those invariants: it inserts or removes body slots together with their members,
/// then <see cref="Equalize"/> adds filler headers so that every path has the same header count, and
/// <see cref="Finish"/> resizes the corner and the tablix.
/// </para>
/// </remarks>
internal sealed class TablixStructure
{
    private const double DefaultRowHeaderWidth = 1.0;

    private const double DefaultColumnHeaderHeight = 0.25;

    private const int MaxGroupsPerAxis = 12;

    private const int MaxSlots = 200;

    private readonly RdlDocument _document;

    private readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase);

    private TablixStructure(RdlDocument document, XElement tablix)
    {
        _document = document;
        Tablix = tablix;
        Ns = document.Ns;
    }

    public XElement Tablix { get; }

    public XNamespace Ns { get; }

    public static TablixStructure Open(RdlDocument document, string table)
    {
        try
        {
            var tablix = TablixNavigator.RequireTablix(document, table);
            if (tablix.Element(document.Ns + "TablixBody")?.Element(document.Ns + "TablixRows") is null
                || tablix.Element(document.Ns + "TablixRowHierarchy")?.Element(document.Ns + "TablixMembers") is null
                || tablix.Element(document.Ns + "TablixColumnHierarchy")?.Element(document.Ns + "TablixMembers") is null)
            {
                throw new ReportDesignException(ReportCodes.InvalidItem);
            }

            return new TablixStructure(document, tablix);
        }
        catch (RdlNotFoundException exception)
        {
            throw new ReportDesignException(ReportCodes.InvalidItem, exception);
        }
    }

    public static bool IsRowAxis(string axis) =>
        axis.Equals("Row", StringComparison.OrdinalIgnoreCase) ? true
        : axis.Equals("Column", StringComparison.OrdinalIgnoreCase) ? false
        : throw new ReportDesignException(ReportCodes.InvalidValue);

    // ------------------------------------------------------------------------------------------------
    // Groups
    // ------------------------------------------------------------------------------------------------

    /// <summary>Adds a group and returns its name.</summary>
    public string InsertGroup(TablixGroupInsert insert)
    {
        var row = IsRowAxis(insert.Axis);
        var root = Root(row);
        if (CountGroups(root) >= MaxGroupsPerAxis)
        {
            throw new ReportDesignException(ReportCodes.Limit);
        }

        var target = ResolveTarget(row, insert.Target);
        var expression = GroupExpression(insert.GroupBy);
        var details = expression.Length == 0;
        if (details && insert.Position is TablixGroupPosition.Parent or TablixGroupPosition.Child)
        {
            throw new ReportDesignException(ReportCodes.InvalidValue);
        }

        var label = FieldOf(expression) ?? (details ? "Details" : "Group");
        var name = GroupName(insert.Name, details ? "Details" : FieldOf(expression) ?? (row ? "RowGroup" : "ColumnGroup"));
        var group = NewGroupMember(name, expression);
        var wantsHeader = insert.HeaderCell && !details;
        var addHeaderBand = insert.AddHeader;
        if (!wantsHeader && !details && insert.Position is TablixGroupPosition.Parent or TablixGroupPosition.Child)
        {
            // A headerless group shows its value in a group header row.
            addHeaderBand = true;
        }

        XElement anchor;

        switch (insert.Position)
        {
            case TablixGroupPosition.Parent:
            {
                target.AddBeforeSelf(group);
                target.Remove();
                group.Add(new XElement(Ns + "TablixMembers", target));
                anchor = group;
                break;
            }

            case TablixGroupPosition.Child:
            {
                if (!IsDynamic(target) || IsDetails(target))
                {
                    // A group cannot be inserted inside the details group or under a static member.
                    throw new ReportDesignException(ReportCodes.InvalidItem);
                }

                var children = target.Element(Ns + "TablixMembers");
                if (children is null)
                {
                    // The target owned a body slot directly; the new child group takes it over.
                    target.Add(new XElement(Ns + "TablixMembers", group));
                }
                else
                {
                    var members = children.Elements(Ns + "TablixMember").ToList();
                    var wrapped = members.Where(member => !IsStaticBand(member)).ToList();
                    if (wrapped.Count == 0)
                    {
                        wrapped = members;
                    }

                    // Keep the wrapped members contiguous: take the first to the last of them.
                    var first = members.IndexOf(wrapped[0]);
                    var last = members.IndexOf(wrapped[^1]);
                    var range = members.GetRange(first, last - first + 1);
                    range[0].AddBeforeSelf(group);
                    var nested = new XElement(Ns + "TablixMembers");
                    foreach (var member in range)
                    {
                        member.Remove();
                        nested.Add(member);
                    }

                    group.Add(nested);
                }

                anchor = group;
                break;
            }

            case TablixGroupPosition.AdjacentBefore:
            case TablixGroupPosition.AdjacentAfter:
            {
                var before = insert.Position == TablixGroupPosition.AdjacentBefore;
                var siblingHeader = HasHeader(target);
                var (first, last) = Range(row, target);
                var slot = before ? first : last + 1;
                if (before)
                {
                    target.AddBeforeSelf(group);
                }
                else
                {
                    target.AddAfterSelf(group);
                }

                // Siblings carry headers together: the new group gets a header cell exactly when its sibling has one.
                wantsHeader = siblingHeader && !details;
                InsertSlot(row, slot, index => index == 0 && !wantsHeader ? expression : "", bold: false);
                if (siblingHeader && details)
                {
                    wantsHeader = true;
                    expression = "";
                }

                anchor = group;
                break;
            }

            default:
                throw new ReportDesignException(ReportCodes.InvalidValue);
        }

        if (wantsHeader)
        {
            anchor.AddFirst(Header(row, SizeAtLevel(row, LevelOf(anchor)), expression, bold: false));
        }

        if (addHeaderBand || insert.AddFooter)
        {
            AddBands(row, group, addHeaderBand, insert.AddFooter, wantsHeader ? "" : expression);
        }

        Equalize(row, fillerLabel: (member, inside) => !inside && IsBeforeGroup(row, member, group) ? label : "");
        Finish();
        return name;
    }

    /// <summary>Adds a total row (or column) before or after a group, outside it.</summary>
    public void AddTotal(string axis, string? target, bool after, Func<string, bool> isNumericField)
    {
        var row = IsRowAxis(axis);
        var member = ResolveTarget(row, target);
        var (first, last) = Range(row, member);
        var source = after ? last : first;
        var total = new XElement(Ns + "TablixMember", new XElement(Ns + "KeepWithGroup", after ? "Before" : "After"));
        if (after)
        {
            member.AddAfterSelf(total);
        }
        else
        {
            member.AddBeforeSelf(total);
        }

        var values = SlotValues(row, source);
        var labelled = false;
        InsertSlot(row, after ? last + 1 : first, index =>
        {
            var value = index < values.Count ? values[index] : "";
            var field = FieldOf(value);
            if (field is not null && isNumericField(field))
            {
                return "=Sum(Fields!" + field + ".Value)";
            }

            if (!labelled && !HasHeader(member))
            {
                labelled = true;
                return "Total";
            }

            return "";
        }, bold: true);

        if (HasHeader(member))
        {
            total.AddFirst(Header(row, SizeAtLevel(row, LevelOf(total)), "Total", bold: true));
        }

        Equalize(row, (_, _) => "");
        Finish();
    }

    /// <summary>Removes a group, either alone or with its own header/footer rows (or columns).</summary>
    public void DeleteGroup(string groupName, TablixDeleteMode mode)
    {
        var (member, row) = FindGroupAnywhere(groupName);
        var children = member.Element(Ns + "TablixMembers")?.Elements(Ns + "TablixMember").ToList() ?? [];

        if (member.Element(Ns + "TablixHeader") is not null)
        {
            var level = LevelOf(member);
            var others = Members(Root(row))
                .Where(other => !ReferenceEquals(other, member) && other.Element(Ns + "TablixHeader") is not null && LevelOf(other) == level)
                .ToList();
            if (others.All(other => !IsDynamic(other)))
            {
                // The header column (or row) belonged to this group only: remove it everywhere.
                foreach (var other in others)
                {
                    other.Element(Ns + "TablixHeader")!.Remove();
                }

                RemoveCornerLevel(row, level);
            }

            member.Element(Ns + "TablixHeader")!.Remove();
        }

        if (children.Count == 0)
        {
            if (mode == TablixDeleteMode.GroupAndRelated)
            {
                RemoveMemberWithSlots(row, member);
            }
            else
            {
                MakeStatic(member);
            }
        }
        else
        {
            if (mode == TablixDeleteMode.GroupAndRelated)
            {
                var bands = children.Where(child => !IsDynamic(child) && child.Element(Ns + "TablixMembers") is null).ToList();
                if (bands.Count < children.Count)
                {
                    foreach (var band in bands)
                    {
                        RemoveMemberWithSlots(row, band);
                    }
                }
            }

            var remaining = member.Element(Ns + "TablixMembers")?.Elements(Ns + "TablixMember").ToList() ?? [];
            foreach (var child in remaining)
            {
                child.Remove();
                member.AddBeforeSelf(child);
            }

            member.Remove();
        }

        CleanUp(row);
        Equalize(row, (_, _) => "");
        Finish();
    }

    public DesignGroupDetail ReadGroup(string groupName)
    {
        var (member, row) = FindGroupAnywhere(groupName);
        var group = member.Element(Ns + "Group")!;
        var parent = member.Ancestors(Ns + "TablixMember").FirstOrDefault(IsDynamic)?.Element(Ns + "Group")?.Attribute("Name")?.Value;
        return new DesignGroupDetail(
            groupName,
            row ? "Row" : "Column",
            group.Element(Ns + "GroupExpressions")?.Elements(Ns + "GroupExpression").Select(node => node.Value).ToArray() ?? [],
            member.Element(Ns + "SortExpressions")?.Elements(Ns + "SortExpression")
                .Select(sort => new DesignSort(sort.Element(Ns + "Value")?.Value ?? "", sort.Element(Ns + "Direction")?.Value ?? "Ascending"))
                .ToArray() ?? [],
            group.Element(Ns + "Filters")?.Elements(Ns + "Filter")
                .Select(filter => new DesignGroupFilter(
                    filter.Element(Ns + "FilterExpression")?.Value ?? "",
                    filter.Element(Ns + "Operator")?.Value ?? "Equal",
                    filter.Element(Ns + "FilterValues")?.Elements(Ns + "FilterValue").Select(value => value.Value).ToArray() ?? []))
                .ToArray() ?? [],
            member.Element(Ns + "Visibility")?.Element(Ns + "Hidden")?.Value ?? "",
            member.Element(Ns + "Visibility")?.Element(Ns + "ToggleItem")?.Value ?? "",
            group.Element(Ns + "PageBreak")?.Element(Ns + "BreakLocation")?.Value ?? "None",
            string.Equals(group.Element(Ns + "PageBreak")?.Element(Ns + "ResetPageNumber")?.Value, "true", StringComparison.OrdinalIgnoreCase),
            group.Element(Ns + "PageName")?.Value ?? "",
            group.Element(Ns + "Variables")?.Elements(Ns + "Variable")
                .Select(variable => new DesignVariable(variable.Attribute("Name")?.Value ?? "", variable.Element(Ns + "Value")?.Value ?? ""))
                .ToArray() ?? [],
            parent,
            IsDetails(member))
        {
            ToggleCandidates = ToggleCandidates(member),
        };
    }

    private IReadOnlyList<string> ToggleCandidates(XElement member)
    {
        var names = new List<string>();
        foreach (var ancestor in member.Ancestors(Ns + "TablixMember").Reverse())
        {
            if (ancestor.Element(Ns + "TablixHeader")?.Element(Ns + "CellContents")?.Element(Ns + "Textbox")?.Attribute("Name")?.Value is { Length: > 0 } name)
            {
                names.Add(name);
            }
        }

        names.AddRange(_document.Root.Descendants(Ns + "Textbox")
            .Where(textbox => !textbox.Ancestors().Any(ancestor => ReferenceEquals(ancestor, Tablix)))
            .Select(textbox => textbox.Attribute("Name")?.Value ?? "")
            .Where(name => name.Length > 0));
        return names;
    }

    public string EditGroup(TablixGroupEdit edit)
    {
        var (member, _) = FindGroupAnywhere(edit.Group);
        var group = member.Element(Ns + "Group")!;
        var name = edit.Group;
        if (edit.NewName is { } newName && !string.Equals(newName, edit.Group, StringComparison.Ordinal))
        {
            if (!ReportDefinitionGuard.ValidIdentifier(newName) || NameInUse(newName))
            {
                throw new ReportDesignException(ReportCodes.InvalidName);
            }

            group.SetAttributeValue("Name", newName);
            RenameScopeReferences(edit.Group, newName);
            name = newName;
        }

        if (edit.GroupExpressions is { } expressions)
        {
            var clean = expressions.Select(value => value.Trim()).Where(value => value.Length > 0).Select(GroupExpression).ToList();
            group.Element(Ns + "GroupExpressions")?.Remove();
            if (clean.Count > 0)
            {
                group.AddFirst(new XElement(Ns + "GroupExpressions", clean.Select(value => new XElement(Ns + "GroupExpression", value))));
            }
        }

        if (edit.Sorts is { } sorts)
        {
            member.Element(Ns + "SortExpressions")?.Remove();
            var valid = sorts.Where(sort => sort.Expression.Trim().Length > 0).ToList();
            if (valid.Count > 0)
            {
                member.Add(new XElement(
                    Ns + "SortExpressions",
                    valid.Select(sort => new XElement(
                        Ns + "SortExpression",
                        new XElement(Ns + "Value", GroupExpression(sort.Expression)),
                        string.Equals(sort.Direction, "Descending", StringComparison.OrdinalIgnoreCase) ? new XElement(Ns + "Direction", "Descending") : null))));
            }
        }

        if (edit.Filters is { } filters)
        {
            group.Element(Ns + "Filters")?.Remove();
            var valid = filters.Where(filter => filter.Expression.Trim().Length > 0).ToList();
            if (valid.Count > 0)
            {
                group.Add(new XElement(
                    Ns + "Filters",
                    valid.Select(filter => new XElement(
                        Ns + "Filter",
                        new XElement(Ns + "FilterExpression", GroupExpression(filter.Expression)),
                        new XElement(Ns + "Operator", FilterOperator(filter.Operator)),
                        new XElement(Ns + "FilterValues", (filter.Values.Count == 0 ? [""] : filter.Values).Select(value => new XElement(Ns + "FilterValue", value)))))));
            }
        }

        if (edit.Hidden is not null || edit.ToggleItem is not null)
        {
            SetVisibility(member, edit.Hidden, edit.ToggleItem);
        }

        if (edit.BreakLocation is not null || edit.ResetPageNumber is not null)
        {
            var pageBreak = group.Element(Ns + "PageBreak") ?? new XElement(Ns + "PageBreak");
            if (pageBreak.Parent is null)
            {
                group.Add(pageBreak);
            }

            if (edit.BreakLocation is { } location)
            {
                pageBreak.SetElementValue(Ns + "BreakLocation", BreakLocation(location));
            }

            if (edit.ResetPageNumber is { } reset)
            {
                pageBreak.SetElementValue(Ns + "ResetPageNumber", reset ? "true" : null);
            }

            if (pageBreak.Element(Ns + "BreakLocation")?.Value is null or "None" && pageBreak.Element(Ns + "ResetPageNumber") is null)
            {
                pageBreak.Remove();
            }
        }

        if (edit.PageName is { } pageName)
        {
            group.SetElementValue(Ns + "PageName", pageName.Trim().Length == 0 ? null : pageName.Trim());
        }

        if (edit.Variables is { } variables)
        {
            group.Element(Ns + "Variables")?.Remove();
            var valid = variables.Where(variable => variable.Name.Trim().Length > 0).ToList();
            if (valid.Any(variable => !ReportDefinitionGuard.ValidIdentifier(variable.Name.Trim())))
            {
                throw new ReportDesignException(ReportCodes.InvalidName);
            }

            if (valid.Count > 0)
            {
                group.Add(new XElement(
                    Ns + "Variables",
                    valid.Select(variable => new XElement(Ns + "Variable", new XAttribute("Name", variable.Name.Trim()), new XElement(Ns + "Value", variable.Value)))));
            }
        }

        return name;
    }

    /// <summary>Sets the properties of the static member that owns a body row (or column).</summary>
    public void EditMember(TablixMemberEdit edit)
    {
        var row = IsRowAxis(edit.Axis);
        var leaves = Leaves(row);
        if (edit.Index < 0 || edit.Index >= leaves.Count)
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        var member = leaves[edit.Index];
        if (edit.KeepWithGroup is { } keep)
        {
            member.SetElementValue(Ns + "KeepWithGroup", keep switch
            {
                "None" or "" => null,
                "Before" or "After" => keep,
                _ => throw new ReportDesignException(ReportCodes.InvalidValue),
            });
        }

        if (edit.RepeatOnNewPage is { } repeat)
        {
            member.SetElementValue(Ns + "RepeatOnNewPage", repeat ? "true" : null);
        }

        if (edit.KeepTogether is { } together)
        {
            member.SetElementValue(Ns + "KeepTogether", together ? "true" : null);
        }

        if (edit.HideIfNoRows is { } hide)
        {
            member.SetElementValue(Ns + "HideIfNoRows", hide ? "true" : null);
        }

        if (edit.Hidden is not null || edit.ToggleItem is not null)
        {
            SetVisibility(member, edit.Hidden, edit.ToggleItem);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Rows, columns and cells
    // ------------------------------------------------------------------------------------------------

    /// <summary>Inserts a body row (or column) next to the one at <paramref name="index"/> and returns the new index.</summary>
    public int InsertSlotNear(string axis, int index, TablixInsertPlacement placement)
    {
        var row = IsRowAxis(axis);
        var leaves = Leaves(row);
        if (index < 0 || index >= leaves.Count)
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        if (leaves.Count >= MaxSlots)
        {
            throw new ReportDesignException(ReportCodes.Limit);
        }

        var leaf = leaves[index];
        var before = placement is TablixInsertPlacement.InsideGroupBefore or TablixInsertPlacement.OutsideGroupBefore;
        int slot;
        if (placement is TablixInsertPlacement.InsideGroupBefore or TablixInsertPlacement.InsideGroupAfter)
        {
            var created = new XElement(Ns + "TablixMember");
            if (IsDynamic(leaf))
            {
                // The new row repeats with this group (for example a second details row): the group keeps
                // its header cell and gets two static children, the old row and the new one.
                var old = new XElement(Ns + "TablixMember");
                leaf.Add(new XElement(Ns + "TablixMembers", before ? [created, old] : [old, created]));
            }
            else if (before)
            {
                leaf.AddBeforeSelf(created);
                CopyKeep(leaf, created);
            }
            else
            {
                leaf.AddAfterSelf(created);
                CopyKeep(leaf, created);
            }

            slot = before ? index : index + 1;
        }
        else
        {
            var scope = leaf.AncestorsAndSelf(Ns + "TablixMember").FirstOrDefault(IsDynamic) ?? leaf;
            var (first, last) = Range(row, scope);
            var created = new XElement(Ns + "TablixMember");
            if (!ReferenceEquals(scope, leaf) || IsDynamic(leaf))
            {
                created.Add(new XElement(Ns + "KeepWithGroup", before ? "After" : "Before"));
            }

            if (before)
            {
                scope.AddBeforeSelf(created);
            }
            else
            {
                scope.AddAfterSelf(created);
            }

            slot = before ? first : last + 1;
        }

        InsertSlot(row, slot, _ => "", bold: false);
        Equalize(row, (_, _) => "");
        Finish();
        return slot;
    }

    /// <summary>Deletes the body row (or column) at <paramref name="index"/>.</summary>
    public void DeleteSlot(string axis, int index, TablixDeleteMode mode)
    {
        var row = IsRowAxis(axis);
        var leaves = Leaves(row);
        if (index < 0 || index >= leaves.Count)
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        var leaf = leaves[index];
        var target = mode == TablixDeleteMode.GroupAndRelated
            ? leaf.AncestorsAndSelf(Ns + "TablixMember").FirstOrDefault(IsDynamic) ?? leaf
            : leaf;
        RemoveMemberWithSlots(row, target);
        CleanUp(row);
        Equalize(row, (_, _) => "");
        Finish();
    }

    /// <summary>Merges body cells <paramref name="firstColumn"/>..<paramref name="lastColumn"/> of a body row.</summary>
    public void MergeCells(int bodyRow, int firstColumn, int lastColumn)
    {
        var rows = TablixNavigator.Rows(Tablix, Ns);
        if (bodyRow < 0 || bodyRow >= rows.Count || firstColumn < 0 || lastColumn <= firstColumn)
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        var cells = TablixNavigator.Cells(rows[bodyRow], Ns);
        if (lastColumn >= cells.Count)
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        var origin = OriginOf(cells, firstColumn);
        var end = lastColumn;
        var originSpan = Span(cells[origin]);
        end = Math.Max(end, origin + originSpan - 1);
        var lastOrigin = OriginOf(cells, lastColumn);
        end = Math.Max(end, lastOrigin + Span(cells[lastOrigin]) - 1);
        if (!CanSpan(Leaves(row: false), origin, end))
        {
            // A merged cell may only span columns that are static siblings in the column hierarchy.
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        for (var column = origin + 1; column <= end; column++)
        {
            if (cells[column].Element(Ns + "CellContents") is not null)
            {
                cells[column].RemoveNodes();
            }
        }

        SetSpan(cells[origin], end - origin + 1);
    }

    /// <summary>Splits a merged body cell back into one cell per column.</summary>
    public void SplitCell(int bodyRow, int column)
    {
        var rows = TablixNavigator.Rows(Tablix, Ns);
        if (bodyRow < 0 || bodyRow >= rows.Count)
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        var cells = TablixNavigator.Cells(rows[bodyRow], Ns);
        if (column < 0 || column >= cells.Count)
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        var origin = OriginOf(cells, column);
        var span = Span(cells[origin]);
        if (span <= 1)
        {
            return;
        }

        SetSpan(cells[origin], 1);
        for (var index = origin + 1; index < origin + span && index < cells.Count; index++)
        {
            cells[index].ReplaceWith(NewCell("", bold: false));
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Reading
    // ------------------------------------------------------------------------------------------------

    public IReadOnlyList<DesignMember> ReadHierarchy(bool row)
    {
        var leaves = Leaves(row);
        return Read(Root(row), 0);

        IReadOnlyList<DesignMember> Read(XElement members, int depth) =>
            members.Elements(Ns + "TablixMember").Select(member =>
            {
                var (first, last) = Range(leaves, member);
                var group = member.Element(Ns + "Group");
                var kind = group is null ? "Static" : group.Element(Ns + "GroupExpressions") is null ? "Details" : "Group";
                var children = member.Element(Ns + "TablixMembers");
                return new DesignMember(
                    kind,
                    group?.Attribute("Name")?.Value ?? "",
                    group?.Element(Ns + "GroupExpressions")?.Elements(Ns + "GroupExpression").FirstOrDefault()?.Value ?? "",
                    depth,
                    first,
                    last,
                    member.Element(Ns + "TablixHeader") is not null,
                    member.Element(Ns + "KeepWithGroup")?.Value ?? "",
                    string.Equals(member.Element(Ns + "RepeatOnNewPage")?.Value, "true", StringComparison.OrdinalIgnoreCase),
                    string.Equals(member.Element(Ns + "Visibility")?.Element(Ns + "Hidden")?.Value, "true", StringComparison.OrdinalIgnoreCase),
                    children is null ? [] : Read(children, depth + 1));
            }).ToArray();
    }

    /// <summary>The drawing grid: corner, header cells and body cells with their spans.</summary>
    public DesignTablixGrid ReadGrid() => ReadGrid(null);

    /// <summary>
    /// The drawing grid, also recording the grid row and column of every cell's <c>CellContents</c>, so items
    /// nested in a cell (a rectangle holding other items, as Business Central list layouts do) can be placed.
    /// </summary>
    internal DesignTablixGrid ReadGrid(Dictionary<XElement, (int Row, int Column)>? origins)
    {
        var rowLevels = Levels(row: true);
        var columnLevels = Levels(row: false);
        var bodyColumns = TablixNavigator.Columns(Tablix, Ns);
        var bodyRows = TablixNavigator.Rows(Tablix, Ns);
        var widths = new List<double>();
        for (var level = 0; level < rowLevels; level++)
        {
            widths.Add(SizeAtLevel(row: true, level));
        }

        widths.AddRange(bodyColumns.Select(column => Positive(Dimension.ToInches(column.Element(Ns + "Width")?.Value), 1)));
        var heights = new List<double>();
        for (var level = 0; level < columnLevels; level++)
        {
            heights.Add(SizeAtLevel(row: false, level));
        }

        heights.AddRange(bodyRows.Select(row => Positive(Dimension.ToInches(row.Element(Ns + "Height")?.Value), 0.25)));
        var cells = new List<DesignGridCell>();
        void Record(XElement? contents, int r, int c)
        {
            if (origins is not null && contents is not null)
            {
                origins[contents] = (r, c);
            }
        }

        // Corner.
        var cornerRows = Tablix.Element(Ns + "TablixCorner")?.Element(Ns + "TablixCornerRows")?.Elements(Ns + "TablixCornerRow").ToList() ?? [];
        for (var r = 0; r < cornerRows.Count && r < columnLevels; r++)
        {
            var cornerCells = cornerRows[r].Elements(Ns + "TablixCornerCell").ToList();
            for (var c = 0; c < cornerCells.Count && c < rowLevels; c++)
            {
                var contents = cornerCells[c].Element(Ns + "CellContents");
                if (contents is null)
                {
                    continue;
                }

                cells.Add(new DesignGridCell(
                    r,
                    c,
                    Math.Clamp(IntOf(contents.Element(Ns + "RowSpan")?.Value, 1), 1, columnLevels - r),
                    Math.Clamp(IntOf(contents.Element(Ns + "ColSpan")?.Value, 1), 1, rowLevels - c),
                    TablixArea.Corner,
                    TextboxName(contents),
                    TextOf(contents)) { FontSize = FontOf(contents), Style = StyleOf(contents) });
                Record(contents, r, c);
            }
        }

        // Row and column headers.
        AddHeaders(row: true);
        AddHeaders(row: false);

        // Body.
        for (var r = 0; r < bodyRows.Count; r++)
        {
            var rowCells = TablixNavigator.Cells(bodyRows[r], Ns);
            for (var c = 0; c < rowCells.Count && c < bodyColumns.Count; c++)
            {
                var contents = rowCells[c].Element(Ns + "CellContents");
                if (contents is null)
                {
                    continue;
                }

                cells.Add(new DesignGridCell(
                    columnLevels + r,
                    rowLevels + c,
                    1,
                    Math.Clamp(Span(rowCells[c]), 1, bodyColumns.Count - c),
                    TablixArea.Body,
                    TextboxName(contents),
                    TextOf(contents),
                    r,
                    c) { FontSize = FontOf(contents), Style = StyleOf(contents) });
                Record(contents, columnLevels + r, rowLevels + c);
            }
        }

        return new DesignTablixGrid(rowLevels, columnLevels, widths, heights, cells);

        void AddHeaders(bool row)
        {
            var levels = row ? rowLevels : columnLevels;
            var offset = row ? columnLevels : rowLevels;
            var leaves = Leaves(row);
            foreach (var member in Members(Root(row)).Where(member => member.Element(Ns + "TablixHeader") is not null))
            {
                var level = LevelOf(member);
                if (level >= levels)
                {
                    continue;
                }

                var (first, last) = Range(leaves, member);
                if (first < 0)
                {
                    continue;
                }

                var deeper = member.Descendants(Ns + "TablixMember").Any(child => child.Element(Ns + "TablixHeader") is not null);
                var cross = deeper ? 1 : levels - level;
                var contents = member.Element(Ns + "TablixHeader")!.Element(Ns + "CellContents");
                var groupName = member.Element(Ns + "Group")?.Attribute("Name")?.Value ?? "";
                cells.Add((row
                    ? new DesignGridCell(offset + first, level, last - first + 1, cross, TablixArea.RowHeader, TextboxName(contents), TextOf(contents), first, -1, groupName)
                    : new DesignGridCell(level, offset + first, cross, last - first + 1, TablixArea.ColumnHeader, TextboxName(contents), TextOf(contents), -1, first, groupName))
                    with { FontSize = FontOf(contents), Style = StyleOf(contents) });
                if (row)
                {
                    Record(contents, offset + first, level);
                }
                else
                {
                    Record(contents, level, offset + first);
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Hierarchy helpers
    // ------------------------------------------------------------------------------------------------

    private XElement Root(bool row) =>
        Tablix.Element(Ns + (row ? "TablixRowHierarchy" : "TablixColumnHierarchy"))!.Element(Ns + "TablixMembers")!;

    internal List<XElement> Leaves(bool row) => Members(Root(row)).Where(member => member.Element(Ns + "TablixMembers") is null).ToList();

    /// <summary>All members of a hierarchy, depth first, in document order.</summary>
    private IEnumerable<XElement> Members(XElement members)
    {
        foreach (var member in members.Elements(Ns + "TablixMember"))
        {
            yield return member;
            if (member.Element(Ns + "TablixMembers") is { } children)
            {
                foreach (var child in Members(children))
                {
                    yield return child;
                }
            }
        }
    }

    private bool IsDynamic(XElement member) => member.Element(Ns + "Group") is not null;

    private bool IsDetails(XElement member) => member.Element(Ns + "Group") is { } group && group.Element(Ns + "GroupExpressions") is null;

    /// <summary>A member that is a group or contains one; such a member cannot keep with a group.</summary>
    private bool IsDynamicLike(XElement member) =>
        IsDynamic(member) || member.Descendants(Ns + "Group").Any();

    private bool HasHeader(XElement member) => member.Element(Ns + "TablixHeader") is not null;

    /// <summary>A static header/footer band of a group: a static leaf kept with its group.</summary>
    private bool IsStaticBand(XElement member) =>
        !IsDynamic(member) && member.Element(Ns + "TablixMembers") is null && member.Element(Ns + "KeepWithGroup") is not null;

    /// <summary>Headers carried by the strict ancestors of a member: the header level the member's own header sits at.</summary>
    private int LevelOf(XElement member) =>
        member.Ancestors(Ns + "TablixMember").Count(ancestor => ancestor.Element(Ns + "TablixHeader") is not null);

    private int HeadersOnPath(XElement leaf) =>
        leaf.AncestorsAndSelf(Ns + "TablixMember").Count(member => member.Element(Ns + "TablixHeader") is not null);

    internal int Levels(bool row)
    {
        var leaves = Leaves(row);
        return leaves.Count == 0 ? 0 : leaves.Max(HeadersOnPath);
    }

    private (int First, int Last) Range(bool row, XElement member) => Range(Leaves(row), member);

    private (int First, int Last) Range(IReadOnlyList<XElement> leaves, XElement member)
    {
        var first = -1;
        var last = -1;
        for (var index = 0; index < leaves.Count; index++)
        {
            if (ReferenceEquals(leaves[index], member) || leaves[index].Ancestors().Any(ancestor => ReferenceEquals(ancestor, member)))
            {
                if (first < 0)
                {
                    first = index;
                }

                last = index;
            }
        }

        return (first, last);
    }

    private XElement ResolveTarget(bool row, string? target)
    {
        var root = Root(row);
        if (string.IsNullOrWhiteSpace(target))
        {
            var members = Members(root).ToList();
            return members.FirstOrDefault(IsDetails)
                ?? members.LastOrDefault(member => IsDynamic(member))
                ?? Leaves(row).LastOrDefault()
                ?? throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        return Members(root).FirstOrDefault(member => string.Equals(member.Element(Ns + "Group")?.Attribute("Name")?.Value, target, StringComparison.Ordinal))
            ?? throw new ReportDesignException(ReportCodes.InvalidItem);
    }

    private (XElement Member, bool Row) FindGroupAnywhere(string groupName)
    {
        foreach (var row in new[] { true, false })
        {
            var found = Members(Root(row)).FirstOrDefault(member => string.Equals(member.Element(Ns + "Group")?.Attribute("Name")?.Value, groupName, StringComparison.Ordinal));
            if (found is not null)
            {
                return (found, row);
            }
        }

        throw new ReportDesignException(ReportCodes.InvalidItem);
    }

    private int CountGroups(XElement root) => Members(root).Count(IsDynamic);

    private bool IsBeforeGroup(bool row, XElement member, XElement group)
    {
        var leaves = Leaves(row);
        var (memberFirst, _) = Range(leaves, member);
        var (groupFirst, _) = Range(leaves, group);
        return memberFirst >= 0 && groupFirst >= 0 && memberFirst < groupFirst && !IsDynamic(member);
    }

    /// <summary>
    /// Makes every root-to-leaf path carry the same number of headers. A path that is short gets a filler
    /// header on the first member whose siblings have one, otherwise on its leaf (or, when the leaf already
    /// has a header, on a new static child of the leaf).
    /// </summary>
    private void Equalize(bool row, Func<XElement, bool, string> fillerLabel)
    {
        for (var guard = 0; guard < 64; guard++)
        {
            var leaves = Leaves(row);
            if (leaves.Count == 0)
            {
                return;
            }

            var target = leaves.Max(HeadersOnPath);
            var shortLeaf = leaves.FirstOrDefault(leaf => HeadersOnPath(leaf) < target);
            if (shortLeaf is null)
            {
                return;
            }

            var path = shortLeaf.AncestorsAndSelf(Ns + "TablixMember").Reverse().ToList();
            var host = path.FirstOrDefault(member => !HasHeader(member)
                && member.Parent!.Elements(Ns + "TablixMember").Any(sibling => !ReferenceEquals(sibling, member) && HasHeader(sibling))
                && LeavesUnder(member).All(leaf => HeadersOnPath(leaf) < target));
            if (host is null)
            {
                host = shortLeaf;
                if (HasHeader(host))
                {
                    var child = new XElement(Ns + "TablixMember");
                    host.Add(new XElement(Ns + "TablixMembers", child));
                    host = child;
                }
            }

            var level = LevelOf(host);
            host.AddFirst(Header(row, SizeAtLevel(row, level), fillerLabel(host, false), bold: true));
        }
    }

    private IEnumerable<XElement> LeavesUnder(XElement member) =>
        member.Element(Ns + "TablixMembers") is { } children
            ? Members(children).Where(child => child.Element(Ns + "TablixMembers") is null)
            : [member];

    private double SizeAtLevel(bool row, int level)
    {
        var fallback = row ? DefaultRowHeaderWidth : DefaultColumnHeaderHeight;
        foreach (var member in Members(Root(row)))
        {
            if (member.Element(Ns + "TablixHeader") is { } header && LevelOf(member) == level)
            {
                return Positive(Dimension.ToInches(header.Element(Ns + "Size")?.Value), fallback);
            }
        }

        return fallback;
    }

    private void MakeStatic(XElement member)
    {
        member.Element(Ns + "Group")?.Remove();
        member.Element(Ns + "SortExpressions")?.Remove();
        member.Element(Ns + "DataElementName")?.Remove();
        member.Element(Ns + "DataElementOutput")?.Remove();
    }

    /// <summary>Removes empty containers and filler wrappers left by a deletion.</summary>
    private void CleanUp(bool row)
    {
        for (var guard = 0; guard < 64; guard++)
        {
            var changed = false;
            foreach (var member in Members(Root(row)).ToList())
            {
                var children = member.Element(Ns + "TablixMembers");
                if (children is not null && !children.Elements(Ns + "TablixMember").Any())
                {
                    // A container left without children owns no slot any more.
                    member.Remove();
                    changed = true;
                    break;
                }

                if (children is not null && !IsDynamic(member) && !HasHeader(member)
                    && member.Elements().All(element => element.Name == Ns + "TablixMembers"))
                {
                    foreach (var child in children.Elements(Ns + "TablixMember").ToList())
                    {
                        child.Remove();
                        member.AddBeforeSelf(child);
                    }

                    member.Remove();
                    changed = true;
                    break;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        if (Leaves(row).Count == 0)
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        // Header levels can drop when the only deep branch is removed; trim headers no path needs.
        var levels = Levels(row);
        foreach (var member in Members(Root(row)).Where(member => HasHeader(member) && LevelOf(member) >= levels).ToList())
        {
            member.Element(Ns + "TablixHeader")!.Remove();
        }
    }

    private void RemoveMemberWithSlots(bool row, XElement member)
    {
        var (first, last) = Range(row, member);
        var total = Leaves(row).Count;
        if (first < 0 || last - first + 1 >= total)
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        for (var index = last; index >= first; index--)
        {
            RemoveSlot(row, index);
        }

        var parent = member.Parent;
        member.Remove();

        // Cascade: a group left without children would own no slot.
        while (parent is not null && !parent.Elements(Ns + "TablixMember").Any() && parent.Parent?.Name == Ns + "TablixMember")
        {
            var owner = parent.Parent;
            parent = owner.Parent;
            owner.Remove();
        }
    }

    private void AddBands(bool row, XElement group, bool header, bool footer, string headerValue)
    {
        var children = group.Element(Ns + "TablixMembers");
        if (children is null)
        {
            // A leaf group gets a static child that keeps its existing slot.
            children = new XElement(Ns + "TablixMembers", new XElement(Ns + "TablixMember"));
            group.Add(children);
        }

        if (header)
        {
            var (first, _) = Range(row, group);
            children.AddFirst(new XElement(Ns + "TablixMember", new XElement(Ns + "KeepWithGroup", "After")));
            InsertSlot(row, first, index => index == 0 ? headerValue : "", bold: true);
        }

        if (footer)
        {
            var (_, last) = Range(row, group);
            children.Add(new XElement(Ns + "TablixMember", new XElement(Ns + "KeepWithGroup", "Before")));
            InsertSlot(row, last + 1, _ => "", bold: false);
        }
    }

    private static void CopyKeep(XElement from, XElement to)
    {
        var ns = from.Name.Namespace;
        if (from.Element(ns + "KeepWithGroup")?.Value is { } keep)
        {
            to.Add(new XElement(ns + "KeepWithGroup", keep));
        }
    }

    private void SetVisibility(XElement member, string? hidden, string? toggle)
    {
        var visibility = member.Element(Ns + "Visibility") ?? new XElement(Ns + "Visibility");
        if (visibility.Parent is null)
        {
            member.Add(visibility);
        }

        if (hidden is not null)
        {
            var value = hidden.Trim();
            if (value.Length > 0 && !value.StartsWith('=') && value is not ("true" or "false"))
            {
                throw new ReportDesignException(ReportCodes.InvalidValue);
            }

            visibility.SetElementValue(Ns + "Hidden", value.Length == 0 || value == "false" ? null : value);
        }

        if (toggle is not null)
        {
            var value = toggle.Trim();
            if (value.Length > 0 && !TextboxExists(value))
            {
                throw new ReportDesignException(ReportCodes.InvalidValue);
            }

            visibility.SetElementValue(Ns + "ToggleItem", value.Length == 0 ? null : value);
        }

        if (!visibility.HasElements)
        {
            visibility.Remove();
        }
    }

    private bool TextboxExists(string name) =>
        _document.Root.Descendants(Ns + "Textbox").Any(textbox => string.Equals(textbox.Attribute("Name")?.Value, name, StringComparison.Ordinal));

    /// <summary>
    /// Whether a scope name is taken. Groups share one name space with report items, datasets and data sources;
    /// fields, parameters, variables and images have their own, so a group may be named after its field.
    /// </summary>
    private bool NameInUse(string name) =>
        _document.Root.Descendants().Any(element =>
            element.Name.LocalName is not ("Field" or "Variable" or "ReportParameter" or "EmbeddedImage" or "QueryParameter" or "CustomProperty")
            && string.Equals(element.Attribute("Name")?.Value, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Renames scope references (<c>Sum(…, "Old")</c>, <c>InScope("Old")</c>, <c>SortExpressionScope</c>…) to a renamed group.</summary>
    private void RenameScopeReferences(string oldName, string newName)
    {
        var quoted = "\"" + oldName + "\"";
        foreach (var node in _document.Root.Descendants().Where(element => !element.HasElements && element.Value.Contains(quoted, StringComparison.Ordinal)))
        {
            node.Value = node.Value.Replace(quoted, "\"" + newName + "\"", StringComparison.Ordinal);
        }

        foreach (var node in _document.Root.Descendants().Where(element => !element.HasElements
            && element.Name.LocalName is "SortExpressionScope" or "SortTarget" or "Scope"
            && string.Equals(element.Value, oldName, StringComparison.Ordinal)))
        {
            node.Value = newName;
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Body slots
    // ------------------------------------------------------------------------------------------------

    private void InsertSlot(bool row, int index, Func<int, string> value, bool bold)
    {
        if (row)
        {
            var rows = Tablix.Element(Ns + "TablixBody")!.Element(Ns + "TablixRows")!;
            var count = TablixNavigator.Columns(Tablix, Ns).Count;
            var cells = new XElement(Ns + "TablixCells");
            for (var column = 0; column < count; column++)
            {
                cells.Add(NewCell(value(column), bold));
            }

            var created = new XElement(Ns + "TablixRow", new XElement(Ns + "Height", "0.25in"), cells);
            var existing = rows.Elements(Ns + "TablixRow").ToList();
            if (index >= existing.Count)
            {
                rows.Add(created);
            }
            else
            {
                existing[Math.Max(0, index)].AddBeforeSelf(created);
            }

            return;
        }

        var definitions = TablixNavigator.RequireColumnsContainer(Tablix, Ns);
        var columns = definitions.Elements(Ns + "TablixColumn").ToList();
        var width = columns.Count == 0 ? "1in" : columns[Math.Clamp(index - 1, 0, columns.Count - 1)].Element(Ns + "Width")?.Value ?? "1in";
        var definition = new XElement(Ns + "TablixColumn", new XElement(Ns + "Width", width));
        if (index >= columns.Count)
        {
            definitions.Add(definition);
        }
        else
        {
            columns[Math.Max(0, index)].AddBeforeSelf(definition);
        }

        var rowIndex = 0;
        foreach (var bodyRow in TablixNavigator.Rows(Tablix, Ns))
        {
            var rowCells = TablixNavigator.Cells(bodyRow, Ns).ToList();
            var container = bodyRow.Element(Ns + "TablixCells")!;
            var text = value(rowIndex++);
            if (index >= rowCells.Count)
            {
                container.Add(NewCell(text, bold));
                continue;
            }

            var origin = OriginOf(rowCells, index);
            if (origin < index)
            {
                // Inside a merged cell: the merge grows to cover the new column.
                SetSpan(rowCells[origin], Span(rowCells[origin]) + 1);
                rowCells[index].AddBeforeSelf(new XElement(Ns + "TablixCell"));
            }
            else
            {
                rowCells[index].AddBeforeSelf(NewCell(text, bold));
            }
        }
    }

    private void RemoveSlot(bool row, int index)
    {
        if (row)
        {
            var rows = TablixNavigator.Rows(Tablix, Ns);
            if (index >= 0 && index < rows.Count)
            {
                rows[index].Remove();
            }

            return;
        }

        var columns = TablixNavigator.Columns(Tablix, Ns);
        if (index >= 0 && index < columns.Count)
        {
            columns[index].Remove();
        }

        foreach (var bodyRow in TablixNavigator.Rows(Tablix, Ns))
        {
            var cells = TablixNavigator.Cells(bodyRow, Ns).ToList();
            if (index >= cells.Count)
            {
                continue;
            }

            var origin = OriginOf(cells, index);
            var span = Span(cells[origin]);
            if (span > 1)
            {
                // Shrink the merge and drop one covered cell; the merged contents stay in the origin cell.
                SetSpan(cells[origin], span - 1);
                var covered = origin == index ? origin + 1 : index;
                if (covered < cells.Count)
                {
                    cells[covered].Remove();
                }
            }
            else
            {
                cells[index].Remove();
            }
        }
    }

    private IReadOnlyList<string> SlotValues(bool row, int index)
    {
        if (row)
        {
            var rows = TablixNavigator.Rows(Tablix, Ns);
            return index >= 0 && index < rows.Count
                ? TablixNavigator.Cells(rows[index], Ns).Select(cell => TablixNavigator.FirstValueText(cell, Ns) ?? "").ToArray()
                : [];
        }

        return TablixNavigator.Rows(Tablix, Ns)
            .Select(bodyRow => TablixNavigator.Cells(bodyRow, Ns))
            .Select(cells => index < cells.Count ? TablixNavigator.FirstValueText(cells[index], Ns) ?? "" : "")
            .ToArray();
    }

    private int OriginOf(IReadOnlyList<XElement> cells, int index)
    {
        var position = 0;
        for (var cell = 0; cell < cells.Count; cell++)
        {
            if (cells[cell].Element(Ns + "CellContents") is null)
            {
                position = cell + 1;
                continue;
            }

            var span = Span(cells[cell]);
            if (index >= cell && index < cell + span)
            {
                return cell;
            }

            position = cell + 1;
        }

        _ = position;
        return index;
    }

    private int Span(XElement cell) => Math.Max(1, IntOf(cell.Element(Ns + "CellContents")?.Element(Ns + "ColSpan")?.Value, 1));

    private void SetSpan(XElement cell, int span)
    {
        var contents = cell.Element(Ns + "CellContents");
        if (contents is null)
        {
            return;
        }

        contents.SetElementValue(Ns + "ColSpan", span > 1 ? span.ToString(CultureInfo.InvariantCulture) : null);
    }

    // ------------------------------------------------------------------------------------------------
    // Finishing: corner, size, body
    // ------------------------------------------------------------------------------------------------

    private void RemoveCornerLevel(bool row, int level)
    {
        var corner = Tablix.Element(Ns + "TablixCorner");
        var cornerRows = corner?.Element(Ns + "TablixCornerRows")?.Elements(Ns + "TablixCornerRow").ToList();
        if (cornerRows is null)
        {
            return;
        }

        if (row)
        {
            foreach (var cornerRow in cornerRows)
            {
                var cornerCells = cornerRow.Elements(Ns + "TablixCornerCell").ToList();
                if (level < cornerCells.Count)
                {
                    cornerCells[level].Remove();
                }
            }
        }
        else if (level < cornerRows.Count)
        {
            cornerRows[level].Remove();
        }
    }

    /// <summary>
    /// Applies the processing engine's rules for static members next to a dynamic sibling: every run of
    /// consecutive static siblings beside a dynamic member agrees on <c>KeepWithGroup</c> and on
    /// <c>RepeatOnNewPage</c>, and column members never keep with a group.
    /// </summary>
    private void NormalizeStatics(bool row)
    {
        if (!row)
        {
            foreach (var keep in Root(row).Descendants(Ns + "KeepWithGroup").ToList())
            {
                keep.Remove();
            }
        }

        foreach (var list in new[] { Root(row) }.Concat(Root(row).Descendants(Ns + "TablixMembers")))
        {
            var siblings = list.Elements(Ns + "TablixMember").ToList();
            foreach (var dynamic in siblings.Where(IsDynamicLike))
            {
                dynamic.Element(Ns + "KeepWithGroup")?.Remove();
            }

            if (!siblings.Any(IsDynamicLike))
            {
                continue;
            }

            var index = 0;
            while (index < siblings.Count)
            {
                if (IsDynamicLike(siblings[index]))
                {
                    index++;
                    continue;
                }

                var start = index;
                while (index < siblings.Count && !IsDynamicLike(siblings[index]))
                {
                    index++;
                }

                var run = siblings.GetRange(start, index - start);
                var dynamicBefore = start > 0;
                var dynamicAfter = index < siblings.Count;
                var keeps = run.Select(member => member.Element(Ns + "KeepWithGroup")?.Value ?? "None").ToList();
                string uniform;
                if (!dynamicBefore)
                {
                    uniform = keeps.Contains("After") ? "After" : "None";
                }
                else if (!dynamicAfter)
                {
                    uniform = keeps.Contains("Before") ? "Before" : "None";
                }
                else
                {
                    var after = keeps.Contains("After");
                    var before = keeps.Contains("Before");
                    uniform = after && !before ? "After" : before && !after ? "Before" : "None";
                }

                var repeat = uniform != "None" && run.Any(member => string.Equals(member.Element(Ns + "RepeatOnNewPage")?.Value, "true", StringComparison.OrdinalIgnoreCase));
                foreach (var member in run)
                {
                    if (row)
                    {
                        member.SetElementValue(Ns + "KeepWithGroup", uniform == "None" ? null : uniform);
                    }

                    if (member.Element(Ns + "RepeatOnNewPage") is not null || repeat)
                    {
                        member.SetElementValue(Ns + "RepeatOnNewPage", repeat ? "true" : null);
                    }
                }
            }
        }
    }

    private bool CanSpan(IReadOnlyList<XElement> columnLeaves, int first, int last)
    {
        if (first < 0 || last >= columnLeaves.Count)
        {
            return false;
        }

        var parent = columnLeaves[first].Parent;
        for (var index = first; index <= last; index++)
        {
            if (IsDynamic(columnLeaves[index]) || !ReferenceEquals(columnLeaves[index].Parent, parent))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Shrinks merged body cells whose span is no longer allowed by the column hierarchy.</summary>
    private void RepairSpans()
    {
        var columnLeaves = Leaves(row: false);
        foreach (var bodyRow in TablixNavigator.Rows(Tablix, Ns))
        {
            var cells = TablixNavigator.Cells(bodyRow, Ns).ToList();
            for (var index = 0; index < cells.Count; index++)
            {
                var span = Span(cells[index]);
                if (span <= 1 || cells[index].Element(Ns + "CellContents") is null)
                {
                    continue;
                }

                var allowed = 1;
                while (allowed < span && CanSpan(columnLeaves, index, index + allowed))
                {
                    allowed++;
                }

                if (allowed == span)
                {
                    continue;
                }

                SetSpan(cells[index], allowed);
                for (var covered = index + allowed; covered < index + span && covered < cells.Count; covered++)
                {
                    if (cells[covered].Element(Ns + "CellContents") is null)
                    {
                        var replacement = NewCell("", bold: false);
                        cells[covered].ReplaceWith(replacement);
                        cells[covered] = replacement;
                    }
                }
            }
        }
    }

    /// <summary>Sizes the corner to the header levels, recomputes the tablix size and the body height.</summary>
    internal void Finish()
    {
        RepairSpans();
        NormalizeStatics(row: true);
        NormalizeStatics(row: false);
        var rowLevels = Levels(row: true);
        var columnLevels = Levels(row: false);
        var corner = Tablix.Element(Ns + "TablixCorner");
        if (rowLevels == 0 || columnLevels == 0)
        {
            corner?.Remove();
        }
        else
        {
            if (corner is null)
            {
                corner = new XElement(Ns + "TablixCorner", new XElement(Ns + "TablixCornerRows"));
                Tablix.Element(Ns + "TablixBody")!.AddBeforeSelf(corner);
            }

            var container = corner.Element(Ns + "TablixCornerRows") ?? new XElement(Ns + "TablixCornerRows");
            if (container.Parent is null)
            {
                corner.Add(container);
            }

            var cornerRows = container.Elements(Ns + "TablixCornerRow").ToList();
            for (var index = cornerRows.Count; index < columnLevels; index++)
            {
                var created = new XElement(Ns + "TablixCornerRow");
                container.Add(created);
                cornerRows.Add(created);
            }

            foreach (var extra in cornerRows.Skip(columnLevels))
            {
                extra.Remove();
            }

            foreach (var cornerRow in cornerRows.Take(columnLevels))
            {
                var cornerCells = cornerRow.Elements(Ns + "TablixCornerCell").ToList();
                for (var index = cornerCells.Count; index < rowLevels; index++)
                {
                    cornerRow.Add(new XElement(Ns + "TablixCornerCell", new XElement(Ns + "CellContents", NewTextbox("", bold: false))));
                }

                foreach (var extra in cornerCells.Skip(rowLevels))
                {
                    extra.Remove();
                }
            }

            ClampCornerSpans(container, rowLevels, columnLevels);
        }

        var width = TablixNavigator.Columns(Tablix, Ns).Sum(column => Dimension.ToInches(column.Element(Ns + "Width")?.Value));
        for (var level = 0; level < rowLevels; level++)
        {
            width += SizeAtLevel(row: true, level);
        }

        var height = TablixNavigator.Rows(Tablix, Ns).Sum(row => Dimension.ToInches(row.Element(Ns + "Height")?.Value));
        for (var level = 0; level < columnLevels; level++)
        {
            height += SizeAtLevel(row: false, level);
        }

        Tablix.SetElementValue(Ns + "Width", Dimension.FromInches(Math.Max(0.1, width)));
        Tablix.SetElementValue(Ns + "Height", Dimension.FromInches(Math.Max(0.1, height)));
        ReportLayoutEditor.FitBody(_document);
    }

    private void ClampCornerSpans(XElement container, int rowLevels, int columnLevels)
    {
        var r = 0;
        foreach (var cornerRow in container.Elements(Ns + "TablixCornerRow"))
        {
            var c = 0;
            foreach (var cornerCell in cornerRow.Elements(Ns + "TablixCornerCell"))
            {
                if (cornerCell.Element(Ns + "CellContents") is { } contents)
                {
                    var colSpan = IntOf(contents.Element(Ns + "ColSpan")?.Value, 1);
                    var rowSpan = IntOf(contents.Element(Ns + "RowSpan")?.Value, 1);
                    if (colSpan > rowLevels - c)
                    {
                        contents.SetElementValue(Ns + "ColSpan", rowLevels - c > 1 ? (rowLevels - c).ToString(CultureInfo.InvariantCulture) : null);
                    }

                    if (rowSpan > columnLevels - r)
                    {
                        contents.SetElementValue(Ns + "RowSpan", columnLevels - r > 1 ? (columnLevels - r).ToString(CultureInfo.InvariantCulture) : null);
                    }
                }

                c++;
            }

            r++;
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Element factories
    // ------------------------------------------------------------------------------------------------

    private XElement NewGroupMember(string name, string expression)
    {
        var group = new XElement(Ns + "Group", new XAttribute("Name", name));
        var member = new XElement(Ns + "TablixMember", group);
        if (expression.Length > 0)
        {
            group.Add(new XElement(Ns + "GroupExpressions", new XElement(Ns + "GroupExpression", expression)));
            member.Add(new XElement(Ns + "SortExpressions", new XElement(Ns + "SortExpression", new XElement(Ns + "Value", expression))));
        }

        _reserved.Add(name);
        return member;
    }

    private XElement Header(bool row, double size, string value, bool bold)
    {
        _ = row;
        return new XElement(
            Ns + "TablixHeader",
            new XElement(Ns + "Size", Dimension.FromInches(size)),
            new XElement(Ns + "CellContents", NewTextbox(value, bold)));
    }

    private XElement NewCell(string value, bool bold) =>
        new(Ns + "TablixCell", new XElement(Ns + "CellContents", NewTextbox(value, bold)));

    private XElement NewTextbox(string value, bool bold)
    {
        var name = ReportLayoutEditor.NextName(_document, "Textbox", _reserved);
        return ReportLayoutEditor.Cell(_document, name, value, bold).Element(Ns + "CellContents")!.Element(Ns + "Textbox")!;
    }

    private string GroupName(string? requested, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var name = requested.Trim();
            if (!ReportDefinitionGuard.ValidIdentifier(name) || NameInUse(name) || _reserved.Contains(name))
            {
                throw new ReportDesignException(ReportCodes.InvalidName);
            }

            return name;
        }

        var stem = ReportDefinitionGuard.ValidIdentifier(fallback) ? fallback : "Group";
        if (!NameInUse(stem) && !_reserved.Contains(stem))
        {
            return stem;
        }

        return ReportLayoutEditor.NextName(_document, stem, _reserved);
    }

    // ------------------------------------------------------------------------------------------------
    // Values
    // ------------------------------------------------------------------------------------------------

    /// <summary>A field name becomes <c>=Fields!Name.Value</c>; an expression is kept; empty stays empty.</summary>
    internal static string GroupExpression(string? value)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0)
        {
            return "";
        }

        if (text.StartsWith('='))
        {
            return text.Length > 2000 ? throw new ReportDesignException(ReportCodes.InvalidValue) : text;
        }

        return ReportDefinitionGuard.ValidIdentifier(text)
            ? "=Fields!" + text + ".Value"
            : throw new ReportDesignException(ReportCodes.InvalidValue);
    }

    /// <summary>The field of a simple <c>=Fields!Name.Value</c> expression.</summary>
    internal static string? FieldOf(string? expression)
    {
        const string prefix = "=Fields!";
        const string suffix = ".Value";
        var text = expression?.Trim() ?? "";
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = text[prefix.Length..^suffix.Length];
        return ReportDefinitionGuard.ValidIdentifier(name) ? name : null;
    }

    private static readonly string[] Operators =
        ["Equal", "Like", "NotEqual", "GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual", "TopN", "BottomN", "TopPercent", "BottomPercent", "In", "Between"];

    private static string FilterOperator(string value) =>
        Operators.FirstOrDefault(candidate => string.Equals(candidate, value?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ReportDesignException(ReportCodes.InvalidValue);

    private static readonly string[] BreakLocations = ["None", "Start", "End", "StartAndEnd", "Between"];

    private static string BreakLocation(string value) =>
        BreakLocations.FirstOrDefault(candidate => string.Equals(candidate, value?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ReportDesignException(ReportCodes.InvalidValue);

    /// <summary>The font size of a cell's text box (first run), such as "7pt"; empty when it has none.</summary>
    private string FontOf(XElement? contents) =>
        contents?.Element(Ns + "Textbox")?.Descendants(Ns + "TextRun").FirstOrDefault()?.Element(Ns + "Style")?.Element(Ns + "FontSize")?.Value ?? "";

    /// <summary>The formatting of a cell's text box (or rectangle), for the designer.</summary>
    private DesignTextStyle StyleOf(XElement? contents) =>
        DesignTextStyle.Read(contents?.Elements().FirstOrDefault(element => element.Name.LocalName is "Textbox" or "Rectangle"));

    private string TextboxName(XElement? contents) =>
        contents?.Element(Ns + "Textbox")?.Attribute("Name")?.Value ?? "";

    private string TextOf(XElement? contents)
    {
        if (contents is null)
        {
            return "";
        }

        if (contents.Element(Ns + "Textbox") is { } textbox)
        {
            return string.Join("", textbox.Descendants(Ns + "TextRun").Select(run => run.Element(Ns + "Value")?.Value ?? ""));
        }

        return contents.Elements().FirstOrDefault() is { } item ? "«" + item.Name.LocalName + "»" : "";
    }

    private static int IntOf(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : fallback;

    private static double Positive(double value, double fallback) => value > 0 ? value : fallback;
}
