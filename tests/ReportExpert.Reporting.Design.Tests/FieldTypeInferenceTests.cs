using System.Xml.Linq;
using ReportExpert.Rdl.Core;

namespace ReportExpert.Reporting.Design.Tests;

/// <summary>
/// Business Central layouts declare fields without rd:TypeName; the preview must still give each field the type
/// its expressions need (DIAGCustStatment: "The input string … was not in a correct format").
/// </summary>
public sealed class FieldTypeInferenceTests
{
    private static readonly IReadOnlyDictionary<string, string> NoCode = new Dictionary<string, string>();

    [Theory]
    [InlineData("=iif(Fields!ShowInternalInfo.Value,FALSE,TRUE)", "ShowInternalInfo", "System.Boolean")]
    [InlineData("=IIF(Not Fields!PrintLines.Value, True, False)", "PrintLines", "System.Boolean")]
    [InlineData("=Fields!IsCopy.Value = True", "IsCopy", "System.Boolean")]
    [InlineData("=Fields!A.Value AndAlso Fields!B.Value", "B", "System.Boolean")]
    [InlineData("=iif(Sum(Fields!RemAmt_IssuedReminderLine.Value),FALSE,TRUE)", "RemAmt_IssuedReminderLine", "System.Decimal")]
    [InlineData("=Sum(CDec(Fields!Amount.Value), \"DataSet_Result\")", "Amount", "System.Decimal")]
    [InlineData("=Fields!Qty.Value * Fields!Price.Value", "Qty", "System.Decimal")]
    [InlineData("=Fields!Qty.Value * Fields!Price.Value", "Price", "System.Decimal")]
    [InlineData("=IIF(Fields!Balance.Value > 0, \"Debit\", \"Credit\")", "Balance", "System.Decimal")]
    [InlineData("=FormatNumber(Fields!Total.Value, 2)", "Total", "System.Decimal")]
    [InlineData("=FormatDateTime(Fields!PostingDate.Value, 2)", "PostingDate", "System.DateTime")]
    // DIAGSalesOrder: the field inside Last(...) compared with 0, and Not on a flag.
    [InlineData("=IIF((last(Fields!NNCVATAmt.Value) <> 0) And (Not Fields!PricesInclVAT.Value),FALSE,TRUE)", "NNCVATAmt", "System.Decimal")]
    [InlineData("=IIF((last(Fields!NNCVATAmt.Value) <> 0) And (Not Fields!PricesInclVAT.Value),FALSE,TRUE)", "PricesInclVAT", "System.Boolean")]
    [InlineData("=IIF(Fields!Print_Bottom_of_page.value =false,false,true)", "Print_Bottom_of_page", "System.Boolean")]
    [InlineData("=iif(Not First(Fields!ShowTotals.Value, \"DataSet_Result\"), True, False)", "ShowTotals", "System.Boolean")]
    [InlineData("=Convert.ToBase64String(Fields!CompanyInfo1Picture.Value)", "CompanyInfo1Picture", "System.Byte[]")]
    public void Usage_implies_the_type(string expression, string field, string expected) =>
        Assert.Equal(expected, ReportFieldTypeInference.Infer(field, expression, NoCode));

    [Theory]
    [InlineData("=iif(Fields!Type_IssuedReminderLine.Value=\"2\",FALSE,TRUE)", "Type_IssuedReminderLine")]
    [InlineData("=Code.SetData(Cstr(Fields!CustAddr1.Value) + Chr(177) + Cstr(Fields!CustAddr2.Value), 1)", "CustAddr1")]
    [InlineData("=Fields!Name_Customer.Value", "Name_Customer")]
    [InlineData("=First(Fields!CustomerListCaption.Value, \"DataSet_Result\")", "CustomerListCaption")]
    [InlineData("=IIF(Fields!DocNoCaption.Value <> \"\" , FALSE, TRUE)", "DocNoCaption")]
    [InlineData("=Fields!Amount2.Value", "Amount")]
    [InlineData("=iif((Fields!AsmInfoExists.Value = \"True\") AND (Fields!Desc.Value <> \"\"),false,true)", "AsmInfoExists")]
    public void Text_use_stays_text(string expression, string field) =>
        Assert.Null(ReportFieldTypeInference.Infer(field, expression, NoCode));

    /// <summary>"…GetData(84,1)" ends one expression and "= Fields!TextFooter1.Value" starts the next: not "1 = field".</summary>
    [Fact]
    public void Neighbouring_expressions_do_not_combine()
    {
        string expressions = string.Join(ReportFieldTypeInference.Separator, "=\"@ : \" + code.GetData(84,1)", "= Fields!TextFooter1.Value");
        Assert.Null(ReportFieldTypeInference.Infer("TextFooter1", expressions, NoCode));
    }

    [Fact]
    public void Adding_to_a_number_makes_a_field_numeric()
    {
        // RemAmt is numeric (T2 sums it); T1 adds CustName to it.
        var document = RdlDocument.Parse(Layout(string.Empty).Replace(
            "=Sum(Fields!Typed.Value) &amp; Fields!CustName.Value",
            "=last(Fields!CustName.Value) + last(Fields!RemAmt.Value)"));

        var fields = ReportFieldTypeInference.FieldsForRender(document, "DataSet_Result").ToDictionary(f => f.Name, f => f.DataType);

        Assert.Equal("System.Decimal", fields["CustName"]);
        Assert.Equal("System.Decimal", fields["RemAmt"]);
    }

    [Fact]
    public void Text_joined_with_plus_stays_text()
    {
        var document = RdlDocument.Parse(Layout(string.Empty).Replace(
            "=Sum(Fields!Typed.Value) &amp; Fields!CustName.Value",
            "=vbcrlf + \" (\" + Fields!CustName.Value + \")\""));

        var fields = ReportFieldTypeInference.FieldsForRender(document, "DataSet_Result").ToDictionary(f => f.Name, f => f.DataType);

        Assert.Equal("System.String", fields["CustName"]);
    }

    [Fact]
    public void Custom_code_parameter_types_are_used()
    {
        var document = RdlDocument.Parse(Layout("<Code>Public Function BlankZero(ByVal Value As Decimal)\nReturn Value\nEnd Function\nPublic Function Flag(ByVal On As Boolean) As String\nReturn \"\"\nEnd Function</Code>"));
        var types = ReportFieldTypeInference.CodeParameterTypes(document);

        Assert.Equal("System.Decimal", ReportFieldTypeInference.Infer("VATAmount", "=Code.BlankZero(Fields!VATAmount.Value)", types));
        Assert.Equal("System.Boolean", ReportFieldTypeInference.Infer("Show", "=Code.Flag(Fields!Show.Value)", types));
    }

    [Fact]
    public void A_bare_field_in_Hidden_is_boolean_but_elsewhere_text()
    {
        Assert.Equal("System.Boolean", ReportFieldTypeInference.Infer("HideTotals", "", NoCode, "=Fields!HideTotals.Value"));
        Assert.Null(ReportFieldTypeInference.Infer("HideTotals", "=Fields!HideTotals.Value", NoCode));
    }

    [Fact]
    public void Declared_types_win_and_untyped_fields_are_inferred()
    {
        var document = RdlDocument.Parse(Layout(string.Empty));

        var fields = ReportFieldTypeInference.FieldsForRender(document, "DataSet_Result").ToDictionary(f => f.Name, f => f.DataType);

        Assert.Equal("System.String", fields["Typed"]);           // declared, even though Sum() is applied to it
        Assert.Equal("System.Boolean", fields["ShowInternalInfo"]);
        Assert.Equal("System.Decimal", fields["RemAmt"]);
        Assert.Equal("System.String", fields["CustName"]);
    }

    /// <summary>The DIAGCustStatment pattern end to end: untyped fields, a Boolean iif and a Sum in Hidden.</summary>
    [Fact]
    public async Task An_untyped_Business_Central_layout_previews_with_sample_data()
    {
        var xml = XDocument.Parse(DesignFixture.BusinessCentralLayout);
        XNamespace ns = xml.Root!.Name.Namespace;
        XNamespace rd = "http://schemas.microsoft.com/SQLServer/reporting/reportdesigner";
        xml.Descendants(rd + "TypeName").Remove();
        xml.Descendants(ns + "Fields").Single().Add(
            new XElement(ns + "Field", new XAttribute("Name", "ShowInternalInfo"), new XElement(ns + "DataField", "ShowInternalInfo")));
        var balance = xml.Descendants(ns + "Textbox").Single(t => (string?)t.Attribute("Name") == "BalanceLCY_Customer");
        balance.Add(new XElement(ns + "Visibility", new XElement(ns + "Hidden", "=iif(Fields!ShowInternalInfo.Value,FALSE,TRUE)")));
        var name = xml.Descendants(ns + "Textbox").Single(t => (string?)t.Attribute("Name") == "Name_Customer");
        name.Add(new XElement(ns + "Visibility", new XElement(ns + "Hidden", "=iif(Sum(Fields!BalanceLCY_Customer.Value) > 0,FALSE,TRUE)")));

        using var fixture = new DesignFixture();
        var model = await fixture.Service.OpenFileAsync(fixture.WriteLayout("Untyped.rdlc", xml.ToString()), CancellationToken.None);
        var output = await fixture.Service.RenderAsync(model.Id, new Dictionary<string, string?>(), ReportOutputFormat.Excel, CancellationToken.None);

        Assert.Equal("PK", System.Text.Encoding.ASCII.GetString(output.Content, 0, 2));
    }

    private static string Layout(string code) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <Report xmlns="http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition" xmlns:rd="http://schemas.microsoft.com/SQLServer/reporting/reportdesigner">
          <DataSets>
            <DataSet Name="DataSet_Result">
              <Query><DataSourceName>DataSource</DataSourceName><CommandText /></Query>
              <Fields>
                <Field Name="Typed"><DataField>Typed</DataField><rd:TypeName>System.String</rd:TypeName></Field>
                <Field Name="ShowInternalInfo"><DataField>ShowInternalInfo</DataField></Field>
                <Field Name="RemAmt"><DataField>RemAmt</DataField></Field>
                <Field Name="CustName"><DataField>CustName</DataField></Field>
              </Fields>
            </DataSet>
          </DataSets>
          <ReportSections><ReportSection><Body><ReportItems>
            <Textbox Name="T1"><Paragraphs><Paragraph><TextRuns><TextRun><Value>=Sum(Fields!Typed.Value) &amp; Fields!CustName.Value</Value></TextRun></TextRuns></Paragraph></Paragraphs>
              <Visibility><Hidden>=iif(Fields!ShowInternalInfo.Value,FALSE,TRUE)</Hidden></Visibility></Textbox>
            <Textbox Name="T2"><Paragraphs><Paragraph><TextRuns><TextRun><Value>=Sum(Fields!RemAmt.Value)</Value></TextRun></TextRuns></Paragraph></Paragraphs></Textbox>
          </ReportItems></Body></ReportSection></ReportSections>
          {code}
        </Report>
        """;
}
