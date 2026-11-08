namespace ReportExpert.Reporting.Design.Tests;

/// <summary>What the format toolbar sets is what the designer reads back to draw (it used to show only in the PDF).</summary>
public sealed class DesignTextStyleTests
{
    [Fact]
    public async Task Text_box_formatting_is_in_the_design_model()
    {
        using var fixture = new DesignFixture();
        var model = await fixture.Service.OpenFileAsync(fixture.WriteLayout(), CancellationToken.None);

        model = await fixture.Service.SetPropertiesAsync(
            model.Id,
            [
                new("TitleTextbox", "Color", "Red"),
                new("TitleTextbox", "FontStyle", "Italic"),
                new("TitleTextbox", "TextDecoration", "Underline"),
                new("TitleTextbox", "FontFamily", "Segoe UI"),
                new("TitleTextbox", "VerticalAlign", "Middle"),
                new("TitleTextbox", "BackgroundColor", "#FFF2CC"),
            ],
            "TitleTextbox",
            CancellationToken.None);

        var style = model.Items.Single(item => item.Name == "TitleTextbox").Style;
        Assert.Equal("Red", style.Color);
        Assert.True(style.Italic);
        Assert.True(style.Underline);
        Assert.Equal("Segoe UI", style.FontFamily);
        Assert.Equal("Middle", style.VerticalAlign);
        Assert.Equal("#FFF2CC", style.BackgroundColor);
    }

    [Fact]
    public async Task Cell_formatting_is_in_the_tablix_grid()
    {
        using var fixture = new DesignFixture();
        var model = await fixture.Service.OpenFileAsync(fixture.WriteLayout(), CancellationToken.None);
        var cell = model.Items.Single(item => item.Name == "CustomerTable").Grid!.Cells.First(candidate => candidate.Area == TablixArea.Body && candidate.TextboxName.Length > 0);

        model = await fixture.Service.SetPropertiesAsync(model.Id, [new(cell.TextboxName, "Color", "#C00000"), new(cell.TextboxName, "FontWeight", "Bold")], cell.TextboxName, CancellationToken.None);

        var updated = model.Items.Single(item => item.Name == "CustomerTable").Grid!.Cells.Single(candidate => candidate.TextboxName == cell.TextboxName);
        Assert.Equal("#C00000", updated.Style.Color);
        Assert.True(updated.Style.Bold);
    }

    [Fact]
    public void Expressions_have_no_design_value()
    {
        var textbox = System.Xml.Linq.XElement.Parse("""
            <Textbox Name="T" xmlns="http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition">
              <Paragraphs><Paragraph><TextRuns><TextRun><Value>x</Value><Style><Color>=IIF(Fields!A.Value &lt; 0, "Red", "Black")</Color></Style></TextRun></TextRuns></Paragraph></Paragraphs>
            </Textbox>
            """);
        Assert.Equal("", DesignTextStyle.Read(textbox).Color);
    }
}
