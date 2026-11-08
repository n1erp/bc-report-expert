using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReportExpert.Core.Reporting;
using Microsoft.Data.Sqlite;
using ReportExpert.Rdl.Core;
using ReportExpert.Storage;
using ReportExpert.Reporting.Export;
using ReportExpert.Reporting.Export.Providers;

namespace ReportExpert.Reporting.Design;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the report design engine for the desktop: export providers, the Business Central dataset
    /// provider, the file-backed workspace store and <see cref="IReportDesignService"/>.
    /// </summary>
    public static IServiceCollection AddReportDesign(this IServiceCollection services, Action<ReportingOptions>? configure = null)
    {
        var builder = services.AddOptions<ReportingOptions>();
        if (configure is not null)
        {
            builder.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IExportProvider, PdfExportProvider>();
        services.AddSingleton<IExportProvider, ExcelExportProvider>();
        services.AddSingleton<IExportProvider, WordExportProvider>();
        services.TryAddSingleton<IReportExportService, ReportExportService>();
        services.TryAddSingleton<ReportWorkspaceContext>();
        services.TryAddSingleton<BusinessCentralDataStore>();
        services.AddSingleton<IReportDataProvider, BusinessCentralDataProvider>();
        services.TryAddSingleton<ReportAuditLog>();
        services.TryAddSingleton<ReportStore>();
        services.TryAddSingleton<ReportDataSources>();
        services.TryAddSingleton<ReportRenderer>();
        services.TryAddSingleton<IReportDesignService, ReportDesignService>();
        return services;
    }
}

/// <summary>
/// Where the desktop keeps its report workspace and who is editing. Replaces SafiPay's tenant and user
/// contexts: one local user, one workspace folder.
/// </summary>
public sealed class ReportWorkspaceContext
{
    public const string LocalTenant = "local";

    public ReportWorkspaceContext(IOptions<ReportingOptions> options)
        : this(options.Value.StoragePath, null)
    {
    }

    public ReportWorkspaceContext(string? rootPath, string? userName)
    {
        RootPath = Path.GetFullPath(string.IsNullOrWhiteSpace(rootPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReportExpert", "reports")
            : rootPath);
        UserName = string.IsNullOrWhiteSpace(userName) ? Environment.UserName : userName;

        // A custom workspace keeps its own database beside it; the default shares the REX app database.
        Database = string.IsNullOrWhiteSpace(rootPath) ? RexDatabase.Default : RexDatabase.At(Path.Combine(RootPath, "rex.db"));
    }

    /// <summary>The SQLite database holding the reports, revisions, audit log and clipboard.</summary>
    public RexDatabase Database { get; }

    /// <summary>Workspace folder. REX 3.0 kept one sub-folder per report here; they are imported into the database once.</summary>
    public string RootPath { get; }

    public string UserName { get; }
}

/// <summary>
/// Appends report actions to the <c>report_audit</c> table of the REX database.
/// </summary>
public sealed class ReportAuditLog(
    ReportWorkspaceContext workspace,
    TimeProvider clock,
    ILogger<ReportAuditLog> logger)
{
    public Task WriteAsync(string action, Guid reportId, string? detail, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Report {Action} {ReportId} user {User}", action, reportId, workspace.UserName);
        using var connection = workspace.Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO report_audit(at, user_name, action, report_id, detail) VALUES ($at, $user, $action, $id, $detail)";
        command.Parameters.AddWithValue("$at", clock.GetUtcNow().ToString("O"));
        command.Parameters.AddWithValue("$user", workspace.UserName);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$id", reportId.ToString("N"));
        command.Parameters.AddWithValue("$detail", (object?)detail ?? DBNull.Value);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }
}

/// <summary>
/// The report workspace in the REX SQLite database: one row per report in <c>reports</c> (current definition,
/// revision, linked RDLC, attached data file) and every revision in <c>report_revisions</c>, so undo and redo
/// step through stored revisions. Ported from SafiPay's file store, which kept the same data as folders.
/// </summary>
public sealed class ReportStore
{
    private readonly IOptions<ReportingOptions> options;
    private readonly ReportWorkspaceContext workspace;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate = new(1, 1);

    public ReportStore(IOptions<ReportingOptions> options, ReportWorkspaceContext workspace, TimeProvider clock)
    {
        this.options = options;
        this.workspace = workspace;
        this.clock = clock;
        LegacyReportImport.Run(workspace);
    }

    private RexDatabase Database => workspace.Database;

    public Task<IReadOnlyList<ReportListItem>> ListAsync(CancellationToken cancellationToken)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, revision, updated_at, updated_by, origin, source_path, definition FROM reports ORDER BY updated_at DESC";
        using var reader = command.ExecuteReader();
        var items = new List<ReportListItem>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(new ReportListItem(
                Guid.ParseExact(reader.GetString(0), "N"),
                reader.GetString(1),
                reader.GetInt32(2),
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                CategoryOf(reader.GetString(7)),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5))
            {
                SourcePath = reader.IsDBNull(6) ? null : reader.GetString(6),
            });
        }

        return Task.FromResult<IReadOnlyList<ReportListItem>>(items);
    }

    public Task<IReadOnlyList<ReportRevisionEntry>> RevisionsAsync(Guid id, CancellationToken cancellationToken)
    {
        var current = Require(id);
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT number, saved_at FROM report_revisions WHERE report_id = $id ORDER BY number DESC";
        command.Parameters.AddWithValue("$id", Key(id));
        using var reader = command.ExecuteReader();
        var entries = new List<ReportRevisionEntry>();
        while (reader.Read())
        {
            var number = reader.GetInt32(0);
            entries.Add(new ReportRevisionEntry(number, DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture), number == current.Revision));
        }

        return Task.FromResult<IReadOnlyList<ReportRevisionEntry>>(entries);
    }

    public Task<StoredReport?> ReadAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Find(id));
    }

    public async Task<StoredReport> CreateAsync(string name, string definition, CancellationToken cancellationToken, string? origin = null)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var id = Guid.NewGuid();
            var now = clock.GetUtcNow();
            using var connection = Database.Open();
            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction, "INSERT INTO reports(id, name, revision, definition, updated_at, updated_by, origin) VALUES ($id, $name, 1, $definition, $at, $user, $origin)",
                ("$id", Key(id)), ("$name", name), ("$definition", definition), ("$at", now.ToString("O")), ("$user", workspace.UserName), ("$origin", origin));
            Execute(connection, transaction, "INSERT INTO report_revisions(report_id, number, definition, saved_at) VALUES ($id, 1, $definition, $at)",
                ("$id", Key(id)), ("$definition", definition), ("$at", now.ToString("O")));
            transaction.Commit();
            return new StoredReport(id, name, 1, definition, now, workspace.UserName);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Stores a new revision: revisions after the current one (redo history) are discarded.</summary>
    public async Task<StoredReport> SaveAsync(Guid id, string name, string definition, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = Require(id);
            var next = current.Revision + 1;
            var now = clock.GetUtcNow();
            using var connection = Database.Open();
            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction, "DELETE FROM report_revisions WHERE report_id = $id AND number >= $next", ("$id", Key(id)), ("$next", next));
            Execute(connection, transaction, "INSERT INTO report_revisions(report_id, number, definition, saved_at) VALUES ($id, $next, $definition, $at)",
                ("$id", Key(id)), ("$next", next), ("$definition", definition), ("$at", now.ToString("O")));
            Execute(connection, transaction, "UPDATE reports SET name = $name, revision = $next, definition = $definition, updated_at = $at, updated_by = $user WHERE id = $id",
                ("$id", Key(id)), ("$name", name), ("$next", next), ("$definition", definition), ("$at", now.ToString("O")), ("$user", workspace.UserName));
            var keep = Math.Max(2, options.Value.MaxVersions);
            Execute(connection, transaction, "DELETE FROM report_revisions WHERE report_id = $id AND number <= $floor", ("$id", Key(id)), ("$floor", next - keep));
            transaction.Commit();
            return new StoredReport(id, name, next, definition, now, workspace.UserName);
        }
        finally
        {
            gate.Release();
        }
    }

    public (bool CanUndo, bool CanRedo) History(Guid id, int revision)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              EXISTS(SELECT 1 FROM report_revisions WHERE report_id = $id AND number = $previous),
              EXISTS(SELECT 1 FROM report_revisions WHERE report_id = $id AND number = $next)
            """;
        command.Parameters.AddWithValue("$id", Key(id));
        command.Parameters.AddWithValue("$previous", revision - 1);
        command.Parameters.AddWithValue("$next", revision + 1);
        using var reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt64(0) == 1, reader.GetInt64(1) == 1);
    }

    /// <summary>Moves to the previous (<c>-1</c>) or next (<c>+1</c>) stored revision.</summary>
    public async Task<StoredReport> StepAsync(Guid id, int direction, IReadOnlySet<string> providerKeys, CancellationToken cancellationToken)
    {
        if (direction is not (-1 or 1))
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = Require(id);
            var target = current.Revision + direction;
            using var connection = Database.Open();
            string? definition;
            using (var read = connection.CreateCommand())
            {
                read.CommandText = "SELECT definition FROM report_revisions WHERE report_id = $id AND number = $number";
                read.Parameters.AddWithValue("$id", Key(id));
                read.Parameters.AddWithValue("$number", target);
                definition = read.ExecuteScalar() as string;
            }

            if (definition is null)
            {
                throw new ReportDesignException(ReportCodes.InvalidItem);
            }

            ReportDefinitionGuard.EnsureSafe(RdlDocument.Parse(definition), providerKeys, options.Value);
            var now = clock.GetUtcNow();
            Execute(connection, null, "UPDATE reports SET revision = $number, definition = $definition, updated_at = $at, updated_by = $user WHERE id = $id",
                ("$id", Key(id)), ("$number", target), ("$definition", definition), ("$at", now.ToString("O")), ("$user", workspace.UserName));
            return new StoredReport(id, current.Name, target, definition, now, workspace.UserName);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Stores a copied report item for the current user.</summary>
    public Task WriteClipboardAsync(string xml, CancellationToken cancellationToken)
    {
        if (xml.Length > options.Value.MaxDefinitionBytes)
        {
            throw new ReportDesignException(ReportCodes.Limit);
        }

        using var connection = Database.Open();
        Execute(connection, null, """
            INSERT INTO report_clipboard(user_name, xml, copied_at) VALUES ($user, $xml, $at)
            ON CONFLICT(user_name) DO UPDATE SET xml = excluded.xml, copied_at = excluded.copied_at
            """, ("$user", workspace.UserName), ("$xml", xml), ("$at", clock.GetUtcNow().ToString("O")));
        return Task.CompletedTask;
    }

    public Task<string?> ReadClipboardAsync(CancellationToken cancellationToken)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT xml FROM report_clipboard WHERE user_name = $user";
        command.Parameters.AddWithValue("$user", workspace.UserName);
        return Task.FromResult(command.ExecuteScalar() as string);
    }

    public bool HasClipboard()
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM report_clipboard WHERE user_name = $user";
        command.Parameters.AddWithValue("$user", workspace.UserName);
        return command.ExecuteScalar() is not null;
    }

    public Task<StoredReport> TouchAsync(Guid id, CancellationToken cancellationToken)
    {
        var current = Require(id);
        var now = clock.GetUtcNow();
        using var connection = Database.Open();
        Execute(connection, null, "UPDATE reports SET updated_at = $at, updated_by = $user WHERE id = $id", ("$id", Key(id)), ("$at", now.ToString("O")), ("$user", workspace.UserName));
        return Task.FromResult(current with { UpdatedAt = now, UpdatedBy = workspace.UserName });
    }

    /// <summary>The report linked to an RDLC file on disk, if the workspace has one.</summary>
    public Task<Guid?> FindBySourceAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var wanted = Path.GetFullPath(sourcePath);
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, source_path FROM reports WHERE source_path IS NOT NULL";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), wanted, PathComparison))
            {
                return Task.FromResult<Guid?>(Guid.ParseExact(reader.GetString(0), "N"));
            }
        }

        return Task.FromResult<Guid?>(null);
    }

    /// <summary>The RDLC file a report is linked to, or null for a report that lives only in the workspace.</summary>
    public Task<string?> SourcePathAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Column(id, "source_path"));

    /// <summary>Links a report to an RDLC file, so Save writes the definition back to it.</summary>
    public Task LinkAsync(Guid id, string? sourcePath, CancellationToken cancellationToken)
    {
        Require(id);
        using var connection = Database.Open();
        Execute(connection, null, "UPDATE reports SET source_path = $path WHERE id = $id", ("$id", Key(id)), ("$path", sourcePath is null ? null : Path.GetFullPath(sourcePath)));
        return Task.CompletedTask;
    }

    /// <summary>The Business Central data file attached to a report, or null.</summary>
    public string? DataFile(Guid id) => Column(id, "data_file");

    public void SetDataFile(Guid id, string? path)
    {
        using var connection = Database.Open();
        Execute(connection, null, "UPDATE reports SET data_file = $path WHERE id = $id", ("$id", Key(id)), ("$path", path));
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            Require(id);
            using var connection = Database.Open();
            Execute(connection, null, "DELETE FROM reports WHERE id = $id", ("$id", Key(id)));
        }
        finally
        {
            gate.Release();
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string Key(Guid id) => id.ToString("N");

    private StoredReport Require(Guid id) => Find(id) ?? throw new ReportDesignException(ReportCodes.NotFound);

    private StoredReport? Find(Guid id)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, revision, definition, updated_at, updated_by FROM reports WHERE id = $id";
        command.Parameters.AddWithValue("$id", Key(id));
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new StoredReport(id, reader.GetString(0), reader.GetInt32(1), reader.GetString(2), DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture), reader.GetString(4))
            : null;
    }

    private string? Column(Guid id, string column)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM reports WHERE id = $id";
        command.Parameters.AddWithValue("$id", Key(id));
        return command.ExecuteScalar() as string;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    private static string? CategoryOf(string definition)
    {
        try
        {
            foreach (var command in RdlDocument.Parse(definition).Descendants("CommandText"))
            {
                if (ReportDataCommand.IsProviderCommand(command.Value))
                {
                    return ReportDataCommand.ProviderKey(command.Value);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }

        return null;
    }
}

/// <summary>
/// Imports the report workspace of REX 3.0 (one folder per report with <c>meta.json</c>, <c>report.rdlc</c>,
/// <c>versions/</c> and <c>bc-data.txt</c>) into the database, once. The folders are left in place.
/// </summary>
internal static class LegacyReportImport
{
    private sealed record LegacyMeta(string Name, int Revision, DateTimeOffset UpdatedAt, string UpdatedBy, string? Origin = null, string? SourcePath = null);

    public static void Run(ReportWorkspaceContext workspace)
    {
        var database = workspace.Database;
        var key = RexStorageKeys.LegacyFilesImported + ".reports";
        if (database.HasKey(key) || !Directory.Exists(workspace.RootPath))
        {
            database.SetJson(key, DateTimeOffset.UtcNow);
            return;
        }

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var folder in Directory.EnumerateDirectories(workspace.RootPath))
        {
            if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out var id))
            {
                continue;
            }

            var metaPath = Path.Combine(folder, "meta.json");
            var definitionPath = Path.Combine(folder, "report.rdlc");
            if (!File.Exists(metaPath) || !File.Exists(definitionPath))
            {
                continue;
            }

            LegacyMeta? meta;
            try
            {
                meta = JsonSerializer.Deserialize<LegacyMeta>(File.ReadAllText(metaPath));
            }
            catch (JsonException)
            {
                continue;
            }

            if (meta is null)
            {
                continue;
            }

            var dataMarker = Path.Combine(folder, "bc-data.txt");
            var dataFile = File.Exists(dataMarker) ? File.ReadAllText(dataMarker).Trim() : null;
            var definition = File.ReadAllText(definitionPath);
            Insert(connection, transaction, "INSERT OR IGNORE INTO reports(id, name, revision, definition, updated_at, updated_by, origin, source_path, data_file) VALUES ($id, $name, $rev, $def, $at, $user, $origin, $source, $data)",
                ("$id", id.ToString("N")), ("$name", meta.Name), ("$rev", meta.Revision), ("$def", definition), ("$at", meta.UpdatedAt.ToString("O")),
                ("$user", meta.UpdatedBy), ("$origin", meta.Origin), ("$source", meta.SourcePath), ("$data", string.IsNullOrEmpty(dataFile) ? null : dataFile));

            var versions = Path.Combine(folder, "versions");
            if (Directory.Exists(versions))
            {
                foreach (var path in Directory.EnumerateFiles(versions, "*.rdlc"))
                {
                    if (int.TryParse(Path.GetFileNameWithoutExtension(path), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number != meta.Revision)
                    {
                        Insert(connection, transaction, "INSERT OR IGNORE INTO report_revisions(report_id, number, definition, saved_at) VALUES ($id, $n, $def, $at)",
                            ("$id", id.ToString("N")), ("$n", number), ("$def", File.ReadAllText(path)), ("$at", File.GetLastWriteTimeUtc(path).ToString("O")));
                    }
                }
            }

            Insert(connection, transaction, "INSERT OR IGNORE INTO report_revisions(report_id, number, definition, saved_at) VALUES ($id, $n, $def, $at)",
                ("$id", id.ToString("N")), ("$n", meta.Revision), ("$def", definition), ("$at", meta.UpdatedAt.ToString("O")));
        }

        transaction.Commit();
        database.SetJson(key, DateTimeOffset.UtcNow);
    }

    private static void Insert(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }
}

public sealed record StoredReport(Guid Id, string Name, int Revision, string Definition, DateTimeOffset UpdatedAt, string UpdatedBy);
