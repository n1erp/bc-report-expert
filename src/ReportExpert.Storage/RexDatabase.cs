using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ReportExpert.Storage;

/// <summary>
/// The REX application database: a single SQLite file (default <c>%LocalAppData%\ReportExpert\rex.db</c>).
/// It holds app settings, recent files and projects, and the report workspace (reports, revisions, audit
/// log, clipboard, attached data files). SQLite runs in-process, so no database server is needed.
/// </summary>
/// <remarks>
/// The database uses write-ahead logging and a busy timeout, so the app and the MCP server can use the same
/// file at once. The schema is versioned with <c>PRAGMA user_version</c> and upgraded on first open.
/// </remarks>
public sealed class RexDatabase
{
    /// <summary>Current schema version. Bump it and add a step to <see cref="Migrate"/> to change the schema.</summary>
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private static readonly Dictionary<string, RexDatabase> Shared = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock SharedLock = new();
    private readonly string connectionString;

    public RexDatabase(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();

        using var connection = Open();
        Migrate(connection);
    }

    /// <summary>The database file.</summary>
    public string Path { get; }

    /// <summary>Default location: <c>%LocalAppData%\ReportExpert\rex.db</c>.</summary>
    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReportExpert", "rex.db");

    /// <summary>The database at <see cref="DefaultPath"/>, shared by every service in the process.</summary>
    public static RexDatabase Default => At(DefaultPath);

    /// <summary>One instance per file, so the schema check runs once per process.</summary>
    public static RexDatabase At(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        lock (SharedLock)
        {
            if (!Shared.TryGetValue(full, out var database))
            {
                database = new RexDatabase(full);
                Shared[full] = database;
            }

            return database;
        }
    }

    /// <summary>Opens a connection with the REX pragmas applied. Dispose it after use.</summary>
    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    /// <summary>Reads a JSON value stored under <paramref name="key"/>, or null.</summary>
    public T? GetJson<T>(string key)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_settings WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<T>(json, Json) : default;
    }

    /// <summary>Stores <paramref name="value"/> as JSON under <paramref name="key"/>.</summary>
    public void SetJson<T>(string key, T value)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_settings(key, value, updated_at) VALUES ($key, $value, $now)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(value, Json));
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    /// <summary>True when a value is stored under <paramref name="key"/>.</summary>
    public bool HasKey(string key)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM app_settings WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() is not null;
    }

    /// <summary>The recent items of one kind (<c>file</c> or <c>project</c>), newest first.</summary>
    public IReadOnlyList<RecentItem> RecentItems(string kind)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT path, name, opened_at FROM recent_items WHERE kind = $kind ORDER BY opened_at DESC";
        command.Parameters.AddWithValue("$kind", kind);
        using var reader = command.ExecuteReader();
        var items = new List<RecentItem>();
        while (reader.Read())
        {
            items.Add(new RecentItem(reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1), DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind)));
        }

        return items;
    }

    /// <summary>Replaces the recent items of one kind with <paramref name="items"/>.</summary>
    public void ReplaceRecentItems(string kind, IEnumerable<RecentItem> items)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM recent_items WHERE kind = $kind";
            delete.Parameters.AddWithValue("$kind", kind);
            delete.ExecuteNonQuery();
        }

        foreach (var item in items)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR REPLACE INTO recent_items(kind, path, name, opened_at) VALUES ($kind, $path, $name, $at)";
            insert.Parameters.AddWithValue("$kind", kind);
            insert.Parameters.AddWithValue("$path", item.Path);
            insert.Parameters.AddWithValue("$name", item.Name);
            insert.Parameters.AddWithValue("$at", item.OpenedUtc.ToUniversalTime().ToString("O"));
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void Migrate(SqliteConnection connection)
    {
        using var read = connection.CreateCommand();
        read.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt32(read.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        if (version >= SchemaVersion)
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (version < 1)
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS app_settings (
                    key         TEXT PRIMARY KEY,
                    value       TEXT NOT NULL,
                    updated_at  TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS recent_items (
                    kind        TEXT NOT NULL,
                    path        TEXT NOT NULL,
                    name        TEXT,
                    opened_at   TEXT NOT NULL,
                    PRIMARY KEY (kind, path)
                );
                CREATE TABLE IF NOT EXISTS reports (
                    id           TEXT PRIMARY KEY,
                    name         TEXT NOT NULL,
                    revision     INTEGER NOT NULL,
                    definition   TEXT NOT NULL,
                    updated_at   TEXT NOT NULL,
                    updated_by   TEXT NOT NULL,
                    origin       TEXT,
                    source_path  TEXT,
                    data_file    TEXT
                );
                CREATE INDEX IF NOT EXISTS ix_reports_source ON reports(source_path);
                CREATE TABLE IF NOT EXISTS report_revisions (
                    report_id   TEXT NOT NULL REFERENCES reports(id) ON DELETE CASCADE,
                    number      INTEGER NOT NULL,
                    definition  TEXT NOT NULL,
                    saved_at    TEXT NOT NULL,
                    PRIMARY KEY (report_id, number)
                );
                CREATE TABLE IF NOT EXISTS report_audit (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    at          TEXT NOT NULL,
                    user_name   TEXT NOT NULL,
                    action      TEXT NOT NULL,
                    report_id   TEXT NOT NULL,
                    detail      TEXT
                );
                CREATE TABLE IF NOT EXISTS report_clipboard (
                    user_name   TEXT PRIMARY KEY,
                    xml         TEXT NOT NULL,
                    copied_at   TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        command.CommandText = $"PRAGMA user_version = {SchemaVersion}";
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}

/// <summary>A recently opened file or project.</summary>
public sealed record RecentItem(string Path, string Name, DateTime OpenedUtc);

/// <summary>Keys and kinds used in the database, shared by the services that read them.</summary>
public static class RexStorageKeys
{
    public const string AppSettings = "app.settings";

    public const string RecentFile = "file";

    public const string RecentProject = "project";

    /// <summary>Marks that the JSON files of an older REX were imported, so they are imported once.</summary>
    public const string LegacyFilesImported = "migration.legacy-files";
}
