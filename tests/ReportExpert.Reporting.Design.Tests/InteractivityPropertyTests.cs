namespace ReportExpert.Reporting.Design.Tests;

/// <summary>Properties added for Report Builder parity: actions, bookmarks, document map, padding, toggles, page breaks.</summary>
public sealed class InteractivityPropertyTests
{
    [Fact]
    public async Task A_table_cell_is_selectable_and_takes_text_box_properties()
    {
        using var fixture = new DesignFixture();
        var model = await fixture.Service.OpenFileAsync(fixture.WriteLayout(), CancellationToken.None);
        var cell = model.Items.Single(item => item.Name == "CustomerTable").Grid!.Cells.First(candidate => candidate.Area == TablixArea.Body);

        model = await fixture.Service.OpenAsync(model.Id, cell.TextboxName, CancellationToken.None) ?? throw new InvalidOperationException();

        Assert.Equal(cell.TextboxName, model.SelectedItem);
        Assert.Equal("Cell", model.SelectedKind);
        var keys = model.Properties!.SelectMany(section => section.Properties).Select(property => property.Key).ToList();
        Assert.Contains("Hyperlink", keys);
        Assert.Contains("PaddingLeft", keys);
        Assert.Contains("ToggleItem", keys);
        Assert.Contains("HideDuplicates", keys);
    }

    [Fact]
    public async Task Actions_bookmarks_padding_and_toggles_are_written_as_rdl()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        using var fixture = new DesignFixture();
        var model = await fixture.Service.OpenFileAsync(fixture.WriteLayout(), CancellationToken.None);
        var cells = model.Items.Single(item => item.Name == "CustomerTable").Grid!.Cells.Where(candidate => candidate.Area == TablixArea.Body).ToList();
        var header = cells.First().TextboxName;
        var detail = cells.Last().TextboxName;

        model = await fixture.Service.SetPropertiesAsync(model.Id,
        [
            new ReportPropertyChange(detail, "Hyperlink", "=\"https://example.com/customer/\" & Fields!No_Customer.Value"),
            new ReportPropertyChange(detail, "Bookmark", "=Fields!No_Customer.Value"),
            new ReportPropertyChange(detail, "DocumentMapLabel", "=Fields!Name_Customer.Value"),
            new ReportPropertyChange(detail, "PaddingLeft", "6"),
            new ReportPropertyChange(detail, "HideDuplicates", "DataSet_Result"),
            new ReportPropertyChange(header, "InitialToggleState", "true"),
        ], detail, CancellationToken.None);

        var definition = System.Text.Encoding.UTF8.GetString((await fixture.Service.DefinitionAsync(model.Id, CancellationToken.None)).Content);
        Assert.Contains("<Hyperlink>=\"https://example.com/customer/\" &amp; Fields!No_Customer.Value</Hyperlink>", definition, StringComparison.Ordinal);
        Assert.Contains("<DocumentMapLabel>=Fields!Name_Customer.Value</DocumentMapLabel>", definition, StringComparison.Ordinal);
        Assert.Contains("<PaddingLeft>6pt</PaddingLeft>", definition, StringComparison.Ordinal);
        Assert.Contains("<HideDuplicates>DataSet_Result</HideDuplicates>", definition, StringComparison.Ordinal);
        Assert.Contains("<InitialState>true</InitialState>", definition, StringComparison.Ordinal);

        // A bookmark link replaces the hyperlink: one action per item.
        model = await fixture.Service.SetPropertyAsync(model.Id, detail, "BookmarkLink", "=Fields!No_Customer.Value", CancellationToken.None);
        definition = System.Text.Encoding.UTF8.GetString((await fixture.Service.DefinitionAsync(model.Id, CancellationToken.None)).Content);
        Assert.DoesNotContain("<Hyperlink>", definition, StringComparison.Ordinal);
        Assert.Contains("<BookmarkLink>", definition, StringComparison.Ordinal);

        var output = await fixture.Service.RenderAsync(model.Id, new Dictionary<string, string?>(), ReportOutputFormat.Excel, CancellationToken.None);
        Assert.Equal("PK", System.Text.Encoding.ASCII.GetString(output.Content, 0, 2));
    }

    [Fact]
    public async Task Showing_an_item_again_keeps_its_toggle_item()
    {
        using var fixture = new DesignFixture();
        var model = await fixture.Service.OpenFileAsync(fixture.WriteLayout(), CancellationToken.None);
        var cells = model.Items.Single(item => item.Name == "CustomerTable").Grid!.Cells.Where(candidate => candidate.Area == TablixArea.Body).ToList();
        var target = cells.Last().TextboxName;

        model = await fixture.Service.SetPropertiesAsync(model.Id,
        [
            new ReportPropertyChange(target, "Hidden", "true"),
            new ReportPropertyChange(target, "ToggleItem", cells.First().TextboxName),
            new ReportPropertyChange(target, "Hidden", "false"),
        ], target, CancellationToken.None);

        var toggle = model.Properties!.SelectMany(section => section.Properties).Single(property => property.Key == "ToggleItem");
        Assert.Equal(cells.First().TextboxName, toggle.Value);
    }
}
