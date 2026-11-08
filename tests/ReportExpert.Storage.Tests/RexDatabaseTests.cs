namespace ReportExpert.Storage.Tests;

public sealed class RexDatabaseTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "rex-storage-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void TheSchemaIsCreatedAndVersioned()
    {
        var database = new RexDatabase(Path.Combine(folder, "rex.db"));
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        Assert.Equal((long)RexDatabase.SchemaVersion, command.ExecuteScalar());

        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name IN ('app_settings','recent_items','reports','report_revisions','report_audit','report_clipboard')";
        Assert.Equal(6L, command.ExecuteScalar());
    }

    [Fact]
    public void SettingsRoundTripAsJson()
    {
        var database = new RexDatabase(Path.Combine(folder, "rex.db"));
        Assert.False(database.HasKey("k"));
        database.SetJson("k", new Sample("Night Sky", 7));
        database.SetJson("k", new Sample("Desert", 8));
        Assert.Equal(new Sample("Desert", 8), database.GetJson<Sample>("k"));
        Assert.True(new RexDatabase(database.Path).HasKey("k"));
    }

    [Fact]
    public void RecentItemsAreReplacedPerKindNewestFirst()
    {
        var database = new RexDatabase(Path.Combine(folder, "rex.db"));
        database.ReplaceRecentItems("project", [new RecentItem("C:/a", "a", DateTime.UtcNow.AddDays(-1)), new RecentItem("C:/b", "b", DateTime.UtcNow)]);
        database.ReplaceRecentItems("file", [new RecentItem("C:/x.rdlc", "x.rdlc", DateTime.UtcNow)]);
        var projects = database.RecentItems("project");
        Assert.Equal(["C:/b", "C:/a"], projects.Select(p => p.Path));
        Assert.Single(database.RecentItems("file"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed record Sample(string Theme, int Count);
}
