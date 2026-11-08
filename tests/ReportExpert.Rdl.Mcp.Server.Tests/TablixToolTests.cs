using ReportExpert.Rdl.Mcp.Server.Tools;

namespace ReportExpert.Rdl.Mcp.Server.Tests;

public sealed class TablixToolTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rex-tablix-tools", Guid.NewGuid().ToString("N"));

    public TablixToolTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Groups_rows_and_merges_round_trip_through_the_tools()
    {
        var path = Path.Combine(_folder, "TablixToolsReport.rdlc");
        File.WriteAllText(path, SampleReport.Xml);
        var table = TablixNames(path).First();

        var structure = TablixTools.GetRdlTablixStructure(path, table);
        Assert.True(structure.Ok, structure.Error?.ToString());

        var dry = TablixTools.InsertRdlTablixGroup(path, table, "Row", "Parent", "=1", group_name: "Everything", dry_run: true);
        Assert.True(dry.Ok, dry.Error?.ToString());
        Assert.DoesNotContain("Everything", File.ReadAllText(path), StringComparison.Ordinal);

        var added = TablixTools.InsertRdlTablixGroup(path, table, "Row", "Parent", "=1", group_name: "Everything", add_footer: true);
        Assert.True(added.Ok, added.Error?.ToString());
        Assert.Contains("Group Name=\"Everything\"", File.ReadAllText(path), StringComparison.Ordinal);

        var edited = TablixTools.EditRdlTablixGroup(path, table, "Everything", break_location: "Between", sort_expressions: ["=1|Descending"]);
        Assert.True(edited.Ok, edited.Error?.ToString());
        Assert.Contains("<BreakLocation>Between</BreakLocation>", File.ReadAllText(path), StringComparison.Ordinal);

        var inserted = TablixTools.InsertRdlTablixRowOrColumn(path, table, "Row", 0, "OutsideGroupBefore");
        Assert.True(inserted.Ok, inserted.Error?.ToString());

        var merged = TablixTools.MergeRdlTablixCells(path, table, 0, 0, 1);
        Assert.True(merged.Ok, merged.Error?.ToString());
        Assert.Contains("<ColSpan>2</ColSpan>", File.ReadAllText(path), StringComparison.Ordinal);

        var deleted = TablixTools.DeleteRdlTablixGroup(path, table, "Everything", "GroupAndRelated");
        Assert.True(deleted.Ok, deleted.Error?.ToString());
        Assert.DoesNotContain("Group Name=\"Everything\"", File.ReadAllText(path), StringComparison.Ordinal);

        var bad = TablixTools.InsertRdlTablixGroup(path, table, "Row", "Sideways", "=1");
        Assert.False(bad.Ok);
    }

    private static IEnumerable<string> TablixNames(string path) =>
        System.Xml.Linq.XDocument.Load(path).Descendants().Where(element => element.Name.LocalName == "Tablix").Select(element => element.Attribute("Name")!.Value);
}
