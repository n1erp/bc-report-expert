using System.Data;
using ReportExpert.Rdl.Core;

namespace ReportExpert.Reporting.Design.Tests;

/// <summary>A preview that fails on an expression says which expression, which fields it reads and what is empty.</summary>
public sealed class ExpressionFailureTests
{
    private const string Layout = """
        <Report xmlns="http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition">
          <ReportSections><ReportSection><Body><ReportItems>
            <Tablix Name="table2">
              <Visibility><Hidden>=IIF(Fields!TrackingQty.Value = 0, True, False)</Hidden></Visibility>
            </Tablix>
            <Textbox Name="Total"><Paragraphs><Paragraph><TextRuns><TextRun><Value>=Sum(Fields!Amount.Value)</Value></TextRun></TextRuns></Paragraph></Paragraphs></Textbox>
          </ReportItems></Body></ReportSection></ReportSections>
        </Report>
        """;

    [Fact]
    public void Names_the_expression_and_its_empty_fields()
    {
        using var table = new DataTable("DataSet_Result");
        table.Columns.Add("TrackingQty", typeof(string));
        table.Rows.Add("");
        table.Rows.Add("2");

        var text = ReportFailure.ExplainExpression(
            RdlDocument.Parse(Layout),
            "The Hidden expression for the tablix ‘table2’ contains an error: The input string '' was not in a correct format.",
            [table]);

        Assert.NotNull(text);
        Assert.Contains("=IIF(Fields!TrackingQty.Value = 0, True, False)", text, StringComparison.Ordinal);
        Assert.Contains("TrackingQty (text, 1 of 2 rows empty)", text, StringComparison.Ordinal);
        Assert.Contains("Guard it", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Finds_a_value_expression()
    {
        var text = ReportFailure.ExplainExpression(
            RdlDocument.Parse(Layout),
            "The Value expression for the textrun 'Total.Paragraphs[0].TextRuns[0]' contains an error",
            []);
        Assert.Null(text);

        text = ReportFailure.ExplainExpression(RdlDocument.Parse(Layout), "The Value expression for the textbox 'Total' contains an error: x", []);
        Assert.NotNull(text);
        Assert.Contains("=Sum(Fields!Amount.Value)", text, StringComparison.Ordinal);
        Assert.Contains("Amount (not in the data)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Other_messages_are_left_alone() =>
        Assert.Null(ReportFailure.ExplainExpression(RdlDocument.Parse(Layout), "The report could not be found.", []));

    [Theory]
    [InlineData("=IIF(Convert.ToInt32(Fields!Qty.Value) > 1, True, False)", "System.Decimal")]
    [InlineData("=IIF(CBool(Fields!Qty.Value), True, False)", "System.Boolean")]
    [InlineData("=Integer.Parse(Fields!Qty.Value)", "System.Decimal")]
    public void Conversions_type_the_field(string expression, string expected) =>
        Assert.Equal(expected, ReportFieldTypeInference.Infer("Qty", expression, new Dictionary<string, string>()));
}
