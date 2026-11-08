namespace ReportExpert.Reporting.Design.Tests;

/// <summary>
/// Items inside a rectangle or a tablix cell (Business Central list layouts) are drawn where they render:
/// their container's position is added to the position RDL stores relative to the container.
/// </summary>
public sealed class NestedItemPlacementTests
{
    private const string Nested = """
          <Rectangle Name="AddressBox">
            <ReportItems>
              <Textbox Name="AddressLine">
                <Paragraphs><Paragraph><TextRuns><TextRun><Value>Address</Value><Style><FontSize>7pt</FontSize></Style></TextRun></TextRuns></Paragraph></Paragraphs>
                <Top>0.25in</Top><Left>0.5in</Left><Height>0.2in</Height><Width>2in</Width>
              </Textbox>
            </ReportItems>
            <Top>2in</Top><Left>1in</Left><Height>1in</Height><Width>4in</Width>
          </Rectangle>
          <Tablix Name="PageList">
            <TablixBody>
              <TablixColumns><TablixColumn><Width>6in</Width></TablixColumn></TablixColumns>
              <TablixRows>
                <TablixRow>
                  <Height>0.5in</Height>
                  <TablixCells><TablixCell><CellContents>
                    <Textbox Name="ListTitle"><Paragraphs><Paragraph><TextRuns><TextRun><Value>Title</Value><Style><FontSize>7pt</FontSize></Style></TextRun></TextRuns></Paragraph></Paragraphs></Textbox>
                  </CellContents></TablixCell></TablixCells>
                </TablixRow>
                <TablixRow>
                  <Height>3in</Height>
                  <TablixCells><TablixCell><CellContents>
                    <Rectangle Name="PageBody">
                      <ReportItems>
                        <Textbox Name="LineText">
                          <Paragraphs><Paragraph><TextRuns><TextRun><Value>Line</Value></TextRun></TextRuns></Paragraph></Paragraphs>
                          <Top>1in</Top><Left>0.5in</Left><Height>0.2in</Height><Width>2in</Width>
                        </Textbox>
                      </ReportItems>
                    </Rectangle>
                  </CellContents></TablixCell></TablixCells>
                </TablixRow>
              </TablixRows>
            </TablixBody>
            <TablixColumnHierarchy><TablixMembers><TablixMember /></TablixMembers></TablixColumnHierarchy>
            <TablixRowHierarchy><TablixMembers><TablixMember /><TablixMember><Group Name="PageGroup"><GroupExpressions><GroupExpression>=Fields!No_Customer.Value</GroupExpression></GroupExpressions></Group></TablixMember></TablixMembers></TablixRowHierarchy>
            <DataSetName>DataSet_Result</DataSetName>
            <Top>4in</Top><Left>0.25in</Left><Height>3.5in</Height><Width>6in</Width>
          </Tablix>
""";

    [Fact]
    public async Task Items_in_a_rectangle_are_placed_inside_it()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);

        var line = model.Items.Single(item => item.Name == "AddressLine");
        Assert.Equal("AddressBox", line.Container);
        Assert.Equal(0.5, line.LeftInches, 3);
        Assert.Equal(1.5, line.AbsoluteLeftInches, 3);
        Assert.Equal(2.25, line.AbsoluteTopInches, 3);
    }

    [Fact]
    public async Task Items_in_a_list_cell_are_placed_in_their_row()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);

        var body = model.Items.Single(item => item.Name == "LineText");
        Assert.Equal("PageBody", body.Container);
        Assert.Equal(0.25 + 0.5, body.AbsoluteLeftInches, 3);
        Assert.Equal(4 + 0.5 + 1, body.AbsoluteTopInches, 3);

        var top = model.Items.Single(item => item.Name == "TitleTextbox");
        Assert.Null(top.Container);
        Assert.Equal(top.LeftInches, top.AbsoluteLeftInches);
    }

    [Fact]
    public async Task Moving_a_nested_item_keeps_its_position_relative_to_the_container()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);
        var line = model.Items.Single(item => item.Name == "AddressLine");

        model = await fixture.Service.MoveAsync(model.Id, line.Name, 0.75, 0.3, line.WidthInches, line.HeightInches, CancellationToken.None);

        line = model.Items.Single(item => item.Name == "AddressLine");
        Assert.Equal(0.75, line.LeftInches, 3);
        Assert.Equal(1.75, line.AbsoluteLeftInches, 3);
        Assert.Equal(2.3, line.AbsoluteTopInches, 3);
    }

    [Fact]
    public async Task Cells_carry_their_text_box_font_size()
    {
        using var fixture = new DesignFixture();
        var model = await OpenAsync(fixture);
        var cells = model.Items.Single(item => item.Name == "PageList").Grid!.Cells;
        Assert.Contains(cells, cell => cell.TextboxName == "ListTitle" && cell.FontSize == "7pt");
    }

    private static async Task<ReportDesignModel> OpenAsync(DesignFixture fixture)
    {
        var layout = DesignFixture.BusinessCentralLayout;
        var at = layout.IndexOf("<Body>", StringComparison.Ordinal);
        var items = layout.IndexOf("<ReportItems>", at, StringComparison.Ordinal) + "<ReportItems>".Length;
        layout = layout.Insert(items, Nested);
        var end = layout.IndexOf("</ReportItems>", layout.IndexOf("PageList", StringComparison.Ordinal), StringComparison.Ordinal);
        var height = layout.IndexOf("<Height>", end, StringComparison.Ordinal);
        var close = layout.IndexOf("</Height>", height, StringComparison.Ordinal);
        layout = layout[..height] + "<Height>9in" + layout[close..];
        return await fixture.Service.OpenFileAsync(fixture.WriteLayout("Nested.rdlc", layout), CancellationToken.None);
    }
}
