using System.Text.Json;
using ReportExpert.Rdl.Mcp.Server.Tools;
using ReportExpert.Reporting.Design;

namespace ReportExpert.Rdl.Mcp.Server.Tests;

/// <summary>The V3 designer tools against a Business Central layout.</summary>
public sealed class DesignToolTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "rex-design-tools", Guid.NewGuid().ToString("N"));
    private readonly string layout;

    public DesignToolTests()
    {
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("REX_DESIGN_WORKSPACE", Path.Combine(Path.GetTempPath(), "rex-design-tools-workspace"));
        layout = Path.Combine(folder, "CustomerList.rdlc");
        File.WriteAllText(layout, ReportTemplates.Read(ReportTemplates.All[0]));
    }

    [Fact]
    public void TheDesignModelListsItemsByRegion()
    {
        var response = DesignTools.GetRdlDesignModel(layout);
        Assert.True(response.Ok, Json(response));
        Assert.Contains("CustomerTable", Json(response), StringComparison.Ordinal);
        Assert.Contains("DataSet_Result", Json(response), StringComparison.Ordinal);
    }

    [Fact]
    public void PlacingAChartWritesTheLayoutAndKeepsTheBusinessCentralCode()
    {
        var dry = DesignTools.PlaceRdlReportItem(layout, "Chart:Pie", 0, -1, dry_run: true);
        Assert.True(dry.Ok, Json(dry));
        Assert.DoesNotContain("<Chart ", File.ReadAllText(layout), StringComparison.Ordinal);

        var real = DesignTools.PlaceRdlReportItem(layout, "Chart:Pie", 0, -1);
        Assert.True(real.Ok, Json(real));
        var xml = File.ReadAllText(layout);
        Assert.Contains("<Chart ", xml, StringComparison.Ordinal);
        Assert.Contains("Public Function SetData", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownItemIsRefused()
    {
        var response = DesignTools.MoveRdlReportItem(layout, "NoSuchItem", 0, 0, 1, 1);
        Assert.False(response.Ok);
    }

    [Fact]
    public void ExcelExportWritesAWorkbook()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var output = Path.Combine(folder, "out.xlsx");
        var response = DesignTools.ExportRdlReport(layout, output, "Excel");
        Assert.True(response.Ok, Json(response));
        Assert.True(new FileInfo(output).Length > 100);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);
}
