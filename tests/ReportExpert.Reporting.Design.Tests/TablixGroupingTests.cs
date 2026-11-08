using System.Xml.Linq;
using ReportExpert.Rdl.Core;

namespace ReportExpert.Reporting.Design.Tests;

/// <summary>
/// The tablix grouping model: parent, child and adjacent groups, group header/footer rows, totals,
/// delete options, insert row/column inside or outside a group, merge/split, and group properties.
/// Every restructured layout is rendered by the report processing engine to prove it is still valid RDL.
/// </summary>
public sealed class TablixGroupingTests
{
    private const string Table = "CustomerTable";

    [Fact]
    public async Task A_parent_row_group_adds_a_row_header_column_labelled_in_the_header_row()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);
        var before = Item(model);

        model = await fixture.Service.InsertGroupAsync(model.Id, new TablixGroupInsert(Table, "Row", null, TablixGroupPosition.Parent, "City_Customer", Name: "City"), CancellationToken.None);

        var table = Item(model);
        Assert.Equal(1, table.Grid!.RowHeaderColumns);
        Assert.Equal(before.Grid!.BodyColumnCount, table.Grid.BodyColumnCount);
        Assert.Equal(before.Grid.BodyRowCount, table.Grid.BodyRowCount);
        Assert.Equal(before.WidthInches + 1, table.WidthInches, 2);

        var group = Assert.Single(table.RowHierarchy!, member => member.Kind == "Group");
        Assert.Equal("City", group.Name);
        Assert.True(group.HasHeader);
        Assert.Equal("Details", Assert.Single(group.Children).Kind);

        var headers = table.Grid.Cells.Where(cell => cell.Area == TablixArea.RowHeader).OrderBy(cell => cell.Row).ToList();
        Assert.Equal(2, headers.Count);
        Assert.Equal("City_Customer", headers[0].Value);
        Assert.Equal("=Fields!City_Customer.Value", headers[1].Value);
        Assert.Equal("City", headers[1].Group);

        await AssertRendersAsync(fixture, model);
        await AssertBusinessCentralContractAsync(fixture, model);
    }

    [Fact]
    public async Task A_headerless_parent_group_shows_its_value_in_a_group_header_row()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);
        var rows = Item(model).Grid!.BodyRowCount;

        model = await fixture.Service.InsertGroupAsync(model.Id, new TablixGroupInsert(Table, "Row", null, TablixGroupPosition.Parent, "City_Customer", Name: "City", HeaderCell: false), CancellationToken.None);

        var table = Item(model);
        Assert.Equal(0, table.Grid!.RowHeaderColumns);
        Assert.Equal(rows + 1, table.Grid.BodyRowCount);
        var group = Assert.Single(table.RowHierarchy!, member => member.Kind == "Group");
        Assert.False(group.HasHeader);
        Assert.Equal(["Static", "Details"], group.Children.Select(child => child.Kind));
        Assert.Equal("After", group.Children[0].KeepWithGroup);
        Assert.Contains(table.Grid.Cells, cell => cell.Area == TablixArea.Body && cell.BodyRow == group.FirstIndex && cell.Value == "=Fields!City_Customer.Value");

        await AssertRendersAsync(fixture, model);
    }

    [Fact]
    public async Task Group_header_and_footer_rows_are_kept_with_their_group()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);

        model = await fixture.Service.InsertGroupAsync(model.Id, new TablixGroupInsert(Table, "Row", null, TablixGroupPosition.Parent, "City_Customer", Name: "City", AddHeader: true, AddFooter: true), CancellationToken.None);

        var group = Assert.Single(Item(model).RowHierarchy!, member => member.Kind == "Group");
        Assert.Equal(["Static", "Details", "Static"], group.Children.Select(child => child.Kind));
        Assert.Equal("After", group.Children[0].KeepWithGroup);
        Assert.Equal("Before", group.Children[2].KeepWithGroup);
        Assert.Equal(3, group.LastIndex - group.FirstIndex + 1);
        await AssertRendersAsync(fixture, model);
    }

    [Fact]
    public async Task A_child_group_nests_under_its_parent_with_a_second_header_level()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);
        model = await fixture.Service.InsertGroupAsync(model.Id, new TablixGroupInsert(Table, "Row", null, TablixGroupPosition.Parent, "City_Customer", Name: "City"), CancellationToken.None);

        model = await fixture.Service.InsertGroupAsync(model.Id, new TablixGroupInsert(Table, "Row", "City", TablixGroupPosition.Child, "Name_Customer", Name: "Customer"), CancellationToken.None);

        var table = Item(model);
        Assert.Equal(2, table.Grid!.RowHeaderColumns);
        var city = Assert.Single(table.RowHierarchy!, member => member.Kind == "Group");
        var name = Assert.Single(city.Children);
        Assert.Equal("Customer", name.Name);
        Assert.Equal("Details", Assert.Single(name.Children).Kind);
        await AssertRendersAsync(fixture, model);
    }

    [Fact]
    public async Task A_child_group_cannot_go_under_the_details_group()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);

        var error = await Assert.ThrowsAsync<ReportDesignException>(() =>
            fixture.Service.InsertGroupAsync(model.Id, new TablixGroupInsert(Table, "Row", "Details", TablixGroupPosition.Child, "City_Customer"), CancellationToken.None));

        Assert.Equal(ReportCodes.InvalidItem, error.Message);
    }

    [Fact]
    public async Task An_adjacent_group_is_a_sibling_with_its_own_row()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);
        model = await fixture.Service.InsertGroupAsync(model.Id, new TablixGroupInsert(Table, "Row", null, TablixGroupPosition.Parent, "City_Customer", Name: "City"), CancellationToken.None);
        var rows = Item(model).Grid!.BodyRowCount;

        model = await fixture.Service.InsertGroupAsync(model.Id, new TablixGroupInsert(Table, "Row", "City", TablixGroupPosition.AdjacentAfter, "No_Customer", Name: "CustomerNo"), CancellationToken.None);

        var table = Item(model);
        Assert.Equal(rows + 1, table.Grid!.BodyRowCount);
        var groups = table.RowHierarchy!.Where(member => member.Kind == "Group").ToList();
        Assert.Equal(["City", "CustomerNo"], groups.Select(group => group.Name));
        Assert.All(groups, group => Assert.True(group.HasHeader));
        Assert.Equal(1, table.Grid.RowHeaderColumns);
        await AssertRendersAsync(fixture, model);
    }

    [Fact]
    public async Task A_total_after_the_details_sums_numeric_fields()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);
        var rows = Item(model).Grid!.BodyRowCount;

        model = await fixture.Service.AddGroupTotalAsync(model.Id, Table, "Row", null, after: true, CancellationToken.None);

        var table = Item(model);
        Assert.Equal(rows + 1, table.Grid!.BodyRowCount);
        var total = table.RowHierarchy!.Last();
        Assert.Equal("Static", total.Kind);
        Assert.Equal("Before", total.KeepWithGroup);
        var cells = table.Grid.Cells.Where(cell => cell.Area == TablixArea.Body && cell.BodyRow == rows).Select(cell => cell.Value).ToList();
        Assert.Contains("Total", cells);
        Assert.Contains("=Sum(Fields!BalanceLCY_Customer.Value)", cells);
        await AssertRendersAsync(fixture, model);
    }

    [Fact]
    public async Task A_group_total_is_labelled_in_the_group_header_column()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);
        model = await fixture.Service.InsertGroupAsync(model.Id, new TablixGroupInsert(Table, "Row", null, TablixGroupPosition.Parent, "City_Customer", Name: "City"), CancellationToken.None);

        model = await fixture.Service.AddGroupTotalAsync(model.Id, Table, "Row", "City", after: true, CancellationToken.None);

        var table = Item(model);
        var total = table.RowHierarchy!.Last();
        Assert.True(total.HasHeader);
        Assert.Contains(table.Grid!.Cells, cell => cell.Area == TablixArea.RowHeader && cell.BodyRow == total.FirstIndex && cell.Value == "Total");
        await AssertRendersAsync(fixture, model);
    }

    [Theory]
    [InlineData(TablixDeleteMode.GroupOnly, 0)]
    [InlineData(TablixDeleteMode.GroupAndRelated, -2)]
    public async Task Deleting_a_group_keeps_or_removes_its_rows(TablixDeleteMode mode, int rowChange)
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);
        var original = Item(model);
        model = await fixture.Service.InsertGroupAsync(model.Id, new TablixGroupInsert(Table, "Row", null, TablixGroupPosition.Parent, "City_Customer", Name: "City", AddHeader: true, AddFooter: true), CancellationToken.None);
        var grouped = Item(model).Grid!.BodyRowCount;

        model = await fixture.Service.DeleteGroupAsync(model.Id, Table, "City", mode, CancellationToken.None);

        var table = Item(model);
        Assert.Equal(grouped + rowChange, table.Grid!.BodyRowCount);
        Assert.Equal(0, table.Grid.RowHeaderColumns);
        Assert.DoesNotContain(table.RowHierarchy!, member => member.Kind == "Group");
        Assert.Equal(original.WidthInches, table.WidthInches, 2);
        await AssertRendersAsync(fixture, model);
    }

    [Fact]
    public async Task Deleting_one_of_two_adjacent_groups_keeps_the_shared_header_column()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);
        model = await fixture.Service.InsertGroupAsync(model.Id, new TablixGroupInsert(Table, "Row", null, TablixGroupPosition.Parent, "City_Customer", Name: "City"), CancellationToken.None);
        model = await fixture.Service.InsertGroupAsync(model.Id, new TablixGroupInsert(Table, "Row", "City", TablixGroupPosition.AdjacentAfter, "No_Customer", Name: "CustomerNo"), CancellationToken.None);

        model = await fixture.Service.DeleteGroupAsync(model.Id, Table, "City", TablixDeleteMode.GroupAndRelated, CancellationToken.None);

        var table = Item(model);
        Assert.Equal(1, table.Grid!.RowHeaderColumns);
        Assert.Equal("CustomerNo", Assert.Single(table.RowHierarchy!, member => member.Kind == "Group").Name);
        await AssertRendersAsync(fixture, model);
    }

    [Fact]
    public async Task Rows_insert_inside_or_outside_the_details_group()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);
        var details = Item(model).RowHierarchy!.Single(member => member.Kind == "Details").FirstIndex;

        model = await fixture.Service.InsertTablixSlotAsync(model.Id, Table, "Row", details, TablixInsertPlacement.InsideGroupAfter, CancellationToken.None);
        var inside = Item(model).RowHierarchy!.Single(member => member.Kind == "Details");
        Assert.Equal(2, inside.Children.Count);
        Assert.Equal(2, inside.LastIndex - inside.FirstIndex + 1);

        model = await fixture.Service.InsertTablixSlotAsync(model.Id, Table, "Row", details, TablixInsertPlacement.OutsideGroupAfter, CancellationToken.None);
        var outside = Item(model).RowHierarchy!.Last();
        Assert.Equal("Static", outside.Kind);
        Assert.Equal("Before", outside.KeepWithGroup);
        await AssertRendersAsync(fixture, model);

        model = await fixture.Service.DeleteTablixSlotAsync(model.Id, Table, "Row", outside.FirstIndex, TablixDeleteMode.GroupOnly, CancellationToken.None);
        Assert.Equal(outside.FirstIndex, Item(model).Grid!.BodyRowCount);
    }

    [Fact]
    public async Task Columns_insert_and_delete_with_their_cells()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);
        var columns = Item(model).Grid!.BodyColumnCount;

        model = await fixture.Service.InsertTablixSlotAsync(model.Id, Table, "Column", 0, TablixInsertPlacement.InsideGroupBefore, CancellationToken.None);
        var table = Item(model);
        Assert.Equal(columns + 1, table.Grid!.BodyColumnCount);
        Assert.All(table.Rows!, row => Assert.Equal(columns + 1, row.Cells.Count));
        await AssertRendersAsync(fixture, model);

        model = await fixture.Service.DeleteTablixSlotAsync(model.Id, Table, "Column", 0, TablixDeleteMode.GroupOnly, CancellationToken.None);
        Assert.Equal(columns, Item(model).Grid!.BodyColumnCount);
    }

    [Fact]
    public async Task The_last_row_cannot_be_deleted()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);
        model = await fixture.Service.DeleteTablixSlotAsync(model.Id, Table, "Row", 0, TablixDeleteMode.GroupOnly, CancellationToken.None);

        await Assert.ThrowsAsync<ReportDesignException>(() =>
            fixture.Service.DeleteTablixSlotAsync(model.Id, Table, "Row", 0, TablixDeleteMode.GroupOnly, CancellationToken.None));
    }

    [Fact]
    public async Task Cells_merge_and_split()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);
        var columns = Item(model).Grid!.BodyColumnCount;

        model = await fixture.Service.MergeTablixCellsAsync(model.Id, Table, 0, 0, 1, CancellationToken.None);
        var merged = Item(model).Grid!.Cells.Single(cell => cell.Area == TablixArea.Body && cell.BodyRow == 0 && cell.BodyColumn == 0);
        Assert.Equal(2, merged.ColSpan);
        Assert.DoesNotContain(Item(model).Grid!.Cells, cell => cell.Area == TablixArea.Body && cell.BodyRow == 0 && cell.BodyColumn == 1);
        await AssertRendersAsync(fixture, model);

        // Inserting a column inside the merge widens it.
        model = await fixture.Service.InsertTablixSlotAsync(model.Id, Table, "Column", 0, TablixInsertPlacement.InsideGroupAfter, CancellationToken.None);
        Assert.Equal(3, Item(model).Grid!.Cells.Single(cell => cell.Area == TablixArea.Body && cell.BodyRow == 0 && cell.BodyColumn == 0).ColSpan);
        await AssertRendersAsync(fixture, model);

        model = await fixture.Service.SplitTablixCellAsync(model.Id, Table, 0, 0, CancellationToken.None);
        var cells = Item(model).Grid!.Cells.Where(cell => cell.Area == TablixArea.Body && cell.BodyRow == 0).ToList();
        Assert.Equal(columns + 1, cells.Count);
        Assert.All(cells, cell => Assert.Equal(1, cell.ColSpan));
        await AssertRendersAsync(fixture, model);
    }

    [Fact]
    public async Task A_column_group_on_a_grouped_table_makes_a_matrix_with_a_corner()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);
        model = await fixture.Service.InsertGroupAsync(model.Id, new TablixGroupInsert(Table, "Row", null, TablixGroupPosition.Parent, "City_Customer", Name: "City"), CancellationToken.None);
        var columnTarget = Item(model).ColumnHierarchy!.Count;
        Assert.True(columnTarget > 0);

        // A parent column group over the last column: every other column gets a filler header cell.
        var definition = await DefinitionAsync(fixture, model);
        var document = RdlDocument.Parse(definition);
        var name = TablixStructure.Open(document, Table).InsertGroup(new TablixGroupInsert(Table, "Column", null, TablixGroupPosition.Parent, "City_Customer", Name: "Cities"));
        Assert.Equal("Cities", name);

        var grid = TablixStructure.Open(document, Table).ReadGrid();
        Assert.Equal(1, grid.ColumnHeaderRows);
        Assert.Equal(1, grid.RowHeaderColumns);
        Assert.Contains(grid.Cells, cell => cell.Area == TablixArea.Corner);
        var tablix = TablixNavigator.RequireTablix(document, Table);
        Assert.NotNull(tablix.Element(document.Ns + "TablixCorner"));

        model = await fixture.Service.ImportAsync("Matrix", document.Serialize(), CancellationToken.None);
        await AssertRendersAsync(fixture, model);
    }

    [Fact]
    public async Task Group_properties_round_trip_and_a_rename_updates_scope_references()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);
        model = await fixture.Service.InsertGroupAsync(model.Id, new TablixGroupInsert(Table, "Row", null, TablixGroupPosition.Parent, "City_Customer", Name: "City", AddFooter: true), CancellationToken.None);
        var footer = Item(model).RowHierarchy!.Single(member => member.Kind == "Group").Children.Last();
        var toggle = Item(model).Grid!.Cells.First(cell => cell.Area == TablixArea.RowHeader && cell.Group == "City").TextboxName;
        model = await fixture.Service.SetPropertyAsync(model.Id, Item(model).Grid!.Cells.First(cell => cell.Area == TablixArea.Body && cell.BodyRow == footer.FirstIndex).TextboxName, "Value", "=Sum(Fields!BalanceLCY_Customer.Value, \"City\")", CancellationToken.None);

        model = await fixture.Service.EditGroupAsync(model.Id, new TablixGroupEdit(
            Table,
            "City",
            NewName: "CityGroup",
            GroupExpressions: ["City_Customer", "=Fields!No_Customer.Value"],
            Sorts: [new DesignSort("=Fields!City_Customer.Value", "Descending")],
            Filters: [new DesignGroupFilter("=Fields!BalanceLCY_Customer.Value", "GreaterThan", ["=0"])],
            BreakLocation: "Between",
            ResetPageNumber: true,
            PageName: "=Fields!City_Customer.Value",
            Variables: [new DesignVariable("CityTotal", "=Sum(Fields!BalanceLCY_Customer.Value)")]), CancellationToken.None);

        // Drilldown: the details start hidden and the group header cell toggles them.
        var details = await fixture.Service.ReadGroupAsync(model.Id, Table, "Details", CancellationToken.None);
        Assert.Contains(toggle, details.ToggleCandidates);
        model = await fixture.Service.EditGroupAsync(model.Id, new TablixGroupEdit(Table, "Details", Hidden: "true", ToggleItem: toggle), CancellationToken.None);
        details = await fixture.Service.ReadGroupAsync(model.Id, Table, "Details", CancellationToken.None);
        Assert.True(details.IsDetails);
        Assert.Equal("true", details.Hidden);
        Assert.Equal(toggle, details.ToggleItem);

        var detail = await fixture.Service.ReadGroupAsync(model.Id, Table, "CityGroup", CancellationToken.None);
        Assert.Equal(["=Fields!City_Customer.Value", "=Fields!No_Customer.Value"], detail.GroupExpressions);
        Assert.Equal("Descending", Assert.Single(detail.Sorts).Direction);
        Assert.Equal("GreaterThan", Assert.Single(detail.Filters).Operator);
        Assert.DoesNotContain(toggle, detail.ToggleCandidates);
        Assert.Equal("Between", detail.BreakLocation);
        Assert.True(detail.ResetPageNumber);
        Assert.Equal("CityTotal", Assert.Single(detail.Variables).Name);
        Assert.False(detail.IsDetails);

        var definition = await DefinitionAsync(fixture, model);
        Assert.Contains("\"CityGroup\")", definition, StringComparison.Ordinal);
        Assert.DoesNotContain("\"City\")", definition, StringComparison.Ordinal);
        await AssertRendersAsync(fixture, model);
    }

    [Fact]
    public async Task Static_members_take_visibility_and_paging_options()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);

        model = await fixture.Service.EditTablixMemberAsync(model.Id, new TablixMemberEdit(Table, "Row", 0, KeepWithGroup: "After", RepeatOnNewPage: true, KeepTogether: true, Hidden: "=Parameters!Missing Is Nothing"), CancellationToken.None);

        var header = Item(model).RowHierarchy![0];
        Assert.True(header.RepeatOnNewPage);
        Assert.Equal("After", header.KeepWithGroup);
        var definition = await DefinitionAsync(fixture, model);
        Assert.Contains("<KeepTogether>true</KeepTogether>", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_structure_change_is_one_undoable_revision()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);
        var original = await DefinitionAsync(fixture, model);

        model = await fixture.Service.InsertGroupAsync(model.Id, new TablixGroupInsert(Table, "Row", null, TablixGroupPosition.Parent, "City_Customer", Name: "City", AddHeader: true, AddFooter: true), CancellationToken.None);
        Assert.True(model.CanUndo);
        model = await fixture.Service.StepAsync(model.Id, -1, CancellationToken.None);

        Assert.Equal(XDocument.Parse(original).ToString(), XDocument.Parse(await DefinitionAsync(fixture, model)).ToString());
    }

    [Theory]
    [InlineData("Customer", "=Fields!Customer.Value")]
    [InlineData("=Fields!A.Value & Fields!B.Value", "=Fields!A.Value & Fields!B.Value")]
    [InlineData("", "")]
    public void Group_expressions_accept_field_names_and_expressions(string input, string expected) =>
        Assert.Equal(expected, TablixStructure.GroupExpression(input));

    [Fact]
    public void An_invalid_field_name_is_rejected() =>
        Assert.Throws<ReportDesignException>(() => TablixStructure.GroupExpression("not a field"));

    private static async Task<ReportDesignModel> OpenAsync(DesignFixture fixture) =>
        await fixture.Service.OpenFileAsync(fixture.WriteLayout(), CancellationToken.None);

    private static DesignItem Item(ReportDesignModel model) => model.Items.Single(item => item.Name == Table);

    private static async Task<string> DefinitionAsync(DesignFixture fixture, ReportDesignModel model) =>
        System.Text.Encoding.UTF8.GetString((await fixture.Service.DefinitionAsync(model.Id, CancellationToken.None)).Content);

    private static async Task AssertRendersAsync(DesignFixture fixture, ReportDesignModel model)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        ReportOutput output;
        try
        {
            output = await fixture.Service.RenderAsync(model.Id, new Dictionary<string, string?>(), ReportOutputFormat.Excel, CancellationToken.None);
        }
        catch (ReportDesignException exception)
        {
            Assert.Fail(exception.Message + ": " + exception.Detail + " " + exception.InnerException?.Message);
            throw;
        }

        Assert.True(output.Content.Length > 100);
        Assert.Equal("PK", System.Text.Encoding.ASCII.GetString(output.Content, 0, 2));
    }

    private static async Task AssertBusinessCentralContractAsync(DesignFixture fixture, ReportDesignModel model)
    {
        var definition = await DefinitionAsync(fixture, model);
        Assert.Contains("DataSet_Result", definition, StringComparison.Ordinal);
        Assert.Contains("<Code>", definition, StringComparison.Ordinal);
        var original = RdlDocument.Parse(DesignFixture.BusinessCentralLayout);
        var current = RdlDocument.Parse(definition);
        Assert.Equal(
            original.Descendants("DataSource").Single().ToString(),
            current.Descendants("DataSource").Single().ToString());
    }
}
