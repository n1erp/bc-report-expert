using System.Data;
using System.Xml.Linq;

namespace ReportExpert.Reporting.Design.Tests;

public sealed class BusinessCentralDataTests
{
    private const string DataItemsXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <ReportDataSet name="Customer - List" id="101">
          <DataItems>
            <DataItem name="Customer">
              <Columns>
                <Column name="CompanyName">CRONUS International Ltd.</Column>
                <Column name="No_Customer">10000</Column>
                <Column name="Name_Customer">Adatum Corporation</Column>
                <Column name="BalanceLCY_Customer" decimalformatter="#,##0.00">1234.5</Column>
              </Columns>
              <DataItems>
                <DataItem name="Ledger">
                  <Columns><Column name="Amount">10</Column></Columns>
                </DataItem>
                <DataItem name="Ledger">
                  <Columns><Column name="Amount">20</Column></Columns>
                </DataItem>
              </DataItems>
            </DataItem>
            <DataItem name="Customer">
              <Columns>
                <Column name="No_Customer">20000</Column>
                <Column name="Name_Customer">Trey Research</Column>
                <Column name="BalanceLCY_Customer">99</Column>
              </Columns>
            </DataItem>
          </DataItems>
        </ReportDataSet>
        """;

    [Fact]
    public void DataItemsXmlIsFlattenedLikeTheBusinessCentralRuntime()
    {
        using var table = BusinessCentralDataReader.ReadXml(XDocument.Parse(DataItemsXml), "DataSet_Result");
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal("Adatum Corporation", table.Rows[1]["Name_Customer"]);
        Assert.Equal("20", table.Rows[1]["Amount"]);
        Assert.Equal("Trey Research", table.Rows[2]["Name_Customer"]);
    }

    [Fact]
    public void ShapingTypesColumnsFromTheLayout()
    {
        var source = BusinessCentralDataReader.ReadXml(XDocument.Parse(DataItemsXml), "DataSet_Result");
        using var shaped = BusinessCentralDataReader.Shape(
            source,
            "DataSet_Result",
            [new ReportField("No_Customer", "System.String"), new ReportField("BalanceLCY_Customer", "System.Decimal"), new ReportField("Missing", "System.Int32")],
            100);

        Assert.Equal(typeof(decimal), shaped.Columns["BalanceLCY_Customer"]!.DataType);
        Assert.Equal(1234.5m, shaped.Rows[0]["BalanceLCY_Customer"]);
        Assert.Equal(DBNull.Value, shaped.Rows[0]["Missing"]);
        Assert.Equal(3, shaped.Rows.Count);
    }

    [Fact]
    public void JsonAndCsvAreAccepted()
    {
        using var json = BusinessCentralDataReader.ReadJson("""{"DataSet_Result":[{"No_Customer":"1","City_Customer":"Rabat"}]}""", "DataSet_Result");
        Assert.Equal("Rabat", json.Rows[0]["City_Customer"]);

        using var csv = BusinessCentralDataReader.ReadCsv("No_Customer,Name_Customer\n1,\"Smith, John\"\n2,Jane\n", "DataSet_Result");
        Assert.Equal(2, csv.Rows.Count);
        Assert.Equal("Smith, John", csv.Rows[0]["Name_Customer"]);
    }

    [Fact]
    public void SampleRowsFollowTheFieldTypes()
    {
        using var sample = BusinessCentralDataReader.Sample("DataSet_Result", [new ReportField("Amount", "System.Decimal"), new ReportField("CustomerListCaption", "System.String"), new ReportField("Posted", "System.DateTime")], 5);
        Assert.Equal(5, sample.Rows.Count);
        Assert.IsType<decimal>(sample.Rows[0]["Amount"]);
        Assert.Equal("Customer List", sample.Rows[0]["CustomerListCaption"]);
        Assert.IsType<DateTime>(sample.Rows[2]["Posted"]);
    }

    [Fact]
    public async Task PreviewUsesTheAttachedFileOrASiblingFile()
    {
        using var fixture = new DesignFixture();
        var layout = fixture.WriteLayout();
        var model = await fixture.Service.OpenFileAsync(layout, CancellationToken.None);

        var sample = await fixture.Service.PreviewDataAsync(model.Id, "DataSet_Result", CancellationToken.None);
        Assert.NotEmpty(sample.Rows);
        Assert.Null(model.DataFile);

        File.WriteAllText(Path.ChangeExtension(layout, ".xml"), DataItemsXml);
        model = (await fixture.Service.OpenAsync(model.Id, null, CancellationToken.None))!;
        Assert.EndsWith("CustomerList.xml", model.DataFile, StringComparison.Ordinal);
        var preview = await fixture.Service.PreviewDataAsync(model.Id, "DataSet_Result", CancellationToken.None);
        Assert.Equal(3, preview.Rows.Count);
        var nameColumn = preview.Columns.ToList().IndexOf("Name_Customer");
        Assert.Equal("Adatum Corporation", preview.Rows[0][nameColumn]);

        var other = Path.Combine(fixture.Root, "other.csv");
        File.WriteAllText(other, "Name_Customer\nOnly one\n");
        model = await fixture.Service.AttachDataAsync(model.Id, other, CancellationToken.None);
        Assert.Equal(other, model.DataFile);
        Assert.Single((await fixture.Service.PreviewDataAsync(model.Id, "DataSet_Result", CancellationToken.None)).Rows);
    }
}

public sealed class UnknownProviderTests
{
    [Fact]
    public async Task ASafiPaySourceFallsBackToTheLayoutFields()
    {
        using var fixture = new DesignFixture();
        var layout = DesignFixture.BusinessCentralLayout.Replace("<CommandText />", "<CommandText>safipay:src.employees</CommandText>", StringComparison.Ordinal);
        var model = await fixture.Service.OpenFileAsync(fixture.WriteLayout("Safi.rdlc", layout), CancellationToken.None);

        Assert.Equal("src.employees", model.DataSets[0].ProviderKey);
        var preview = await fixture.Service.PreviewDataAsync(model.Id, "DataSet_Result", CancellationToken.None);
        Assert.NotEmpty(preview.Rows);

        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var output = await fixture.Service.RenderAsync(model.Id, new Dictionary<string, string?>(), ReportOutputFormat.Excel, CancellationToken.None);
        Assert.True(output.Content.Length > 100);
    }
}
