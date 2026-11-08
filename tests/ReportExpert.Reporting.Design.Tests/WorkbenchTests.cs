using System.Xml.Linq;
using ReportExpert.Rdl.Core;

namespace ReportExpert.Reporting.Design.Tests;

/// <summary>The Report Builder-style workbench: placeholders, page setup, the blank-page rule and batch edits.</summary>
public sealed class WorkbenchTests
{
    [Theory]
    [InlineData("=Fields!Amount.Value", "[Amount]")]
    [InlineData("=Sum(Fields!Amount.Value)", "[Sum(Amount)]")]
    [InlineData("=First(Fields!Name_Customer.Value, \"DataSet_Result\")", "[First(Name_Customer)]")]
    [InlineData("=Parameters!Year.Value", "[@Year]")]
    [InlineData("=Parameters!Year.Label", "[@Year.Label]")]
    [InlineData("=Globals!PageNumber", "[&PageNumber]")]
    [InlineData("=\"Invoice\"", "Invoice")]
    [InlineData("=code.GetData(14,1)", "«Expr»")]
    [InlineData("=Fields!A.Value & \" \" & Fields!B.Value", "«Expr»")]
    [InlineData("Plain caption", "Plain caption")]
    [InlineData("", "")]
    public void Values_are_shown_as_Report_Builder_placeholders(string value, string expected) =>
        Assert.Equal(expected, ReportExpressionDisplay.Placeholder(value));

    [Fact]
    public void Page_setup_is_read_in_the_layout_unit()
    {
        var document = RdlDocument.Parse(Layout(width: "21cm", height: "29.7cm", left: "1cm", right: "1cm", body: "19cm", unit: "Cm"));

        var page = ReportPageSetup.Read(document);

        Assert.Equal("A4", page.PaperSize);
        Assert.Equal("Portrait", page.Orientation);
        Assert.True(page.IsMetric);
        Assert.Equal(19 / 2.54, page.PrintableWidthInches, 3);
        Assert.Null(ReportPageSetup.BlankPageWarning(page, 19 / 2.54));
    }

    [Fact]
    public void A_body_wider_than_the_page_minus_margins_warns_about_blank_pages()
    {
        var page = ReportPageSetup.Read(RdlDocument.Parse(Layout(width: "21cm", height: "29.7cm", left: "0.8cm", right: "1cm", body: "19.4cm", unit: "Cm")));

        var warning = ReportPageSetup.BlankPageWarning(page, 19.4 / 2.54);

        Assert.NotNull(warning);
        Assert.Contains("19.4cm", warning);
        Assert.Contains("blank pages", warning);
    }

    [Fact]
    public void Columns_share_the_printable_width()
    {
        var page = new DesignPageSetup("Letter", "Portrait", 8.5, 11, 0.5, 0.5, 0.5, 0.5, Columns: 2, ColumnSpacingInches: 0.5, "Inch");
        Assert.Equal(3.5, page.MaxBodyWidthInches, 3);
    }

    [Fact]
    public async Task Page_setup_changes_are_one_revision_and_reported_in_the_model()
    {
        using var fixture = new DesignFixture();
        var model = await fixture.Service.OpenFileAsync(fixture.WriteLayout(), CancellationToken.None);
        var revision = model.Revision;
        const string Report = ReportPageSetup.ReportTarget;

        var updated = await fixture.Service.SetPropertiesAsync(model.Id,
        [
            new(Report, "PageWidth", "11.69in"),
            new(Report, "PageHeight", "8.27in"),
            new(Report, "LeftMargin", "0.4in"),
            new(Report, "RightMargin", "0.4in"),
            new(Report, "TopMargin", "0.3in"),
            new(Report, "BottomMargin", "0.3in"),
        ], null, CancellationToken.None);

        Assert.Equal(revision + 1, updated.Revision);
        Assert.NotNull(updated.Page);
        Assert.Equal("A4", updated.Page!.PaperSize);
        Assert.Equal("Landscape", updated.Page.Orientation);
        Assert.Equal(0.4, updated.Page.LeftMarginInches, 3);

        var undone = await fixture.Service.StepAsync(model.Id, -1, CancellationToken.None);
        Assert.Equal(model.Page!.PageWidthInches, undone.Page!.PageWidthInches, 3);
    }

    [Fact]
    public async Task Fit_body_to_page_removes_the_blank_page_warning()
    {
        using var fixture = new DesignFixture();
        var model = await fixture.Service.OpenFileAsync(fixture.WriteLayout(), CancellationToken.None);
        // The template's table is 7.5in on an A4 page with 0.38in margins (7.51in printable); a 9in body overflows.
        var wide = await fixture.Service.SetPropertiesAsync(model.Id,
            [new(ReportPageSetup.ReportTarget, "ReportWidth", "9in")], null, CancellationToken.None);
        Assert.Contains(wide.Warnings, warning => warning.Contains("blank pages", StringComparison.Ordinal));

        var fitted = await fixture.Service.FitBodyToPageAsync(model.Id, CancellationToken.None);

        Assert.DoesNotContain(fitted.Warnings, warning => warning.Contains("blank pages", StringComparison.Ordinal));
        Assert.True(fitted.BodyWidthInches <= fitted.Page!.MaxBodyWidthInches + 0.01);
    }

    [Fact]
    public async Task Formatting_several_items_is_one_revision()
    {
        using var fixture = new DesignFixture();
        var model = await fixture.Service.OpenFileAsync(fixture.WriteLayout(), CancellationToken.None);
        var a = await fixture.Service.PlaceAsync(model.Id, "Textbox", 0.1, 0.1, CancellationToken.None, "Body");
        var b = await fixture.Service.PlaceAsync(model.Id, "Textbox", 2, 0.1, CancellationToken.None, "Body");
        var names = new[] { a.SelectedItem!, b.SelectedItem! };

        var styled = await fixture.Service.SetPropertiesAsync(model.Id, names.Select(name => new ReportPropertyChange(name, "FontWeight", "Bold")).ToArray(), names[0], CancellationToken.None);

        Assert.Equal(b.Revision + 1, styled.Revision);
        Assert.All(styled.Items.Where(item => names.Contains(item.Name)), item => Assert.True(item.Bold));
        Assert.Equal(names[0], styled.SelectedItem);
    }

    [Fact]
    public async Task Fields_and_expressions_land_in_the_band_they_are_dropped_on()
    {
        using var fixture = new DesignFixture();
        var model = await fixture.Service.OpenFileAsync(fixture.WriteLayout(), CancellationToken.None);
        var set = model.DataSets[0];

        var field = await fixture.Service.PlaceInAsync(model.Id, $"Field:{set.Name}/{set.Fields[0].Name}", 0.2, 0.1, "Header", null, CancellationToken.None);
        Assert.Equal("Header", field.Items.Single(item => item.Name == field.SelectedItem).Region);

        var page = await fixture.Service.PlaceInAsync(model.Id, "Expr:=Globals!PageNumber", 0.2, 0.05, "Footer", null, CancellationToken.None);
        var item = page.Items.Single(candidate => candidate.Name == page.SelectedItem);
        Assert.Equal("Footer", item.Region);
        Assert.Equal("=Globals!PageNumber", item.Value);

        await Assert.ThrowsAsync<ReportDesignException>(() =>
            fixture.Service.PlaceInAsync(model.Id, "Expr:not an expression", 0, 0, "Body", null, CancellationToken.None));
    }

    private static string Layout(string width, string height, string left, string right, string body, string unit) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <Report xmlns="http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition" xmlns:rd="http://schemas.microsoft.com/SQLServer/reporting/reportdesigner">
          <ReportSections><ReportSection>
            <Body><ReportItems /><Height>2cm</Height></Body>
            <Width>{body}</Width>
            <Page><PageHeight>{height}</PageHeight><PageWidth>{width}</PageWidth><LeftMargin>{left}</LeftMargin><RightMargin>{right}</RightMargin><TopMargin>1cm</TopMargin><BottomMargin>1cm</BottomMargin></Page>
          </ReportSection></ReportSections>
          <rd:ReportUnitType>{unit}</rd:ReportUnitType>
        </Report>
        """;
}
