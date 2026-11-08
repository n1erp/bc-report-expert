using System.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReportExpert.Core.Reporting;
using ReportExpert.Domain.Export;
using ReportExpert.Rdl.Core;
using ReportExpert.Rdl.Core.Reading;
using ReportExpert.Rdl.Core.Validation;

namespace ReportExpert.Reporting.Design;

public sealed class ReportRenderer(
    IReportExportService export,
    ReportDataSources sources,
    IOptions<ReportingOptions> options,
    ILogger<ReportRenderer> logger)
{
    private static readonly SemaphoreSlim Gate = new(2, 2);

    static ReportRenderer()
    {
        // Layout code such as Chr(177) uses Windows code pages, which .NET only has once this provider is registered.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    private static readonly string[] IgnoredIssues = ["No datasets found", "No Tablix (table) found"];

    public async Task<ReportOutput> RenderAsync(StoredReport report, IReadOnlyDictionary<string, string?> parameters, ReportOutputFormat format, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var document = RdlDocument.Parse(report.Definition);
        var known = await sources.KeysAsync(cancellationToken);
        ReportDefinitionGuard.NormalizeDataSources(document);
        ReportDefinitionGuard.EnsureSafe(document, known, settings);
        var validation = RdlValidator.Validate(document);
        var issues = (validation.Issues ?? []).Where(issue => !IgnoredIssues.Contains(issue, StringComparer.Ordinal)).ToArray();
        if (issues.Length > 0)
        {
            throw new ReportDesignException(ReportCodes.InvalidDefinition, ReportFailure.Limit(string.Join(" ", issues)));
        }

        // Resolve parameters before taking a render slot, so a missing or malformed value fails fast with its own code.
        var exportParameters = ResolveParameters(document, parameters);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(settings.ExecutionTimeoutSeconds, 1, 120));
        using var timed = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timed.CancelAfter(timeout);
        try
        {
            if (!await Gate.WaitAsync(timeout, timed.Token))
            {
                throw new ReportDesignException(ReportCodes.Limit);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReportDesignException(ReportCodes.Limit);
        }

        var folder = Path.Combine(Path.GetTempPath(), "rex-report", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var definitionPath = Path.Combine(folder, "report.rdlc");
            var outputPath = Path.Combine(folder, "output" + Extension(format));
            ReportChartEditor.NormalizeForRender(document);
            ReportDefinitionGuard.RemoveEmptyCollections(document);
            await File.WriteAllTextAsync(definitionPath, document.Serialize(), timed.Token);
            var tables = new List<DataTable>();
            try
            {
                var values = ProviderParameters(document, exportParameters, DateTime.Today);
                tables.AddRange(await BindAsync(report.Id, document, values, settings.MaxRows, settings.MaxSourceRows, timed.Token));
                await export.ExportAsync(definitionPath, tables, exportParameters, ToExport(format), outputPath, timed.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ReportDesignException(ReportCodes.Limit);
            }
            catch (ReportDesignException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Report {ReportId} failed to render as {Format}", report.Id, format);
                var detail = ReportFailure.Describe(exception);
                if (ReportFailure.ExplainExpression(document, detail, tables) is { } explained)
                {
                    detail = detail + " " + explained;
                }

                throw new ReportDesignException(ReportCodes.RenderFailed, detail, exception);
            }
            finally
            {
                foreach (var table in tables)
                {
                    table.Dispose();
                }
            }

            var info = new FileInfo(outputPath);
            if (!info.Exists || info.Length == 0 || info.Length > settings.MaxExportBytes)
            {
                throw new ReportDesignException(ReportCodes.Limit);
            }

            var bytes = await File.ReadAllBytesAsync(outputPath, cancellationToken);
            return new ReportOutput(bytes, ContentType(format), FileName(report.Name, format));
        }
        finally
        {
            Gate.Release();
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch (IOException exception)
            {
                logger.LogDebug(exception, "Temporary report folder {Folder} could not be removed", folder);
            }
        }
    }

    private async Task<IReadOnlyList<DataTable>> BindAsync(Guid reportId, RdlDocument document, IReadOnlyDictionary<string, string?> parameters, int maxRows, int maxSourceRows, CancellationToken cancellationToken)
    {
        var tables = new List<DataTable>();
        foreach (var dataset in DataSetReader.GetDataSets(document).DataSets)
        {
            var key = ReportDataCommand.ProviderKey(dataset.CommandText);
            var provider = await sources.FindAsync(key, cancellationToken)
                ?? throw new ReportDesignException(ReportCodes.UnknownProvider);
            // Untyped fields (every Business Central layout) get the type the layout's expressions need.
            var declared = ReportFieldTypeInference.FieldsForRender(document, dataset.Name);
            var fields = declared.Select(field => field.Name).ToArray();
            var request = new ReportDataSetRequest(reportId, dataset.Name, declared);
            DataTable table;
            try
            {
                var cap = provider is IReportSourceProvider ? Math.Clamp(maxSourceRows, 1, 200_000) : Math.Clamp(maxRows, 1, 5000);
                table = await ReportDataSources.ReadAsync(provider, fields, parameters, cap, cancellationToken, request);
            }
            catch (Exception exception) when (exception is not (ReportDesignException or OperationCanceledException))
            {
                // The database message stays in the log: it can name servers, logins or columns.
                logger.LogWarning(exception, "Dataset {DataSet} ({Source}) could not be read", dataset.Name, key);
                throw new ReportDesignException(ReportCodes.DataUnavailable, $"The dataset \"{dataset.Name}\" ({key}) could not be read. The application log has the database error.", exception);
            }

            table.TableName = dataset.Name;
            tables.Add(table);
        }

        return tables;
    }

    /// <summary>
    /// Builds the values passed to the renderer. A blank value falls back to the literal default;
    /// a parameter whose default is an expression is left out so the renderer evaluates it.
    /// </summary>
    internal static Dictionary<string, object?> ResolveParameters(RdlDocument document, IReadOnlyDictionary<string, string?> parameters)
    {
        var resolved = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var parameter in ReportParameterEditor.ReadAll(document))
        {
            parameters.TryGetValue(parameter.Name, out var raw);
            if (ReportParameterEditor.Resolve(parameter, raw, out var value))
            {
                resolved[parameter.Name] = value;
            }
        }

        return resolved;
    }

    /// <summary>
    /// The parameter values data sources receive: what the user chose, the literal default otherwise, and for an
    /// expression default the value of the common date expressions (<see cref="ReportDefaultExpressions"/>), so a
    /// source filters on the same period the report shows. Multiple values are joined with the separator.
    /// </summary>
    internal static Dictionary<string, string?> ProviderParameters(RdlDocument document, IReadOnlyDictionary<string, object?> resolved, DateTime today)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var parameter in ReportParameterEditor.ReadAll(document))
        {
            if (resolved.TryGetValue(parameter.Name, out var value))
            {
                values[parameter.Name] = value switch
                {
                    null => null,
                    string[] many => string.Join(ReportParameterValues.Separator, many),
                    IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                    _ => value.ToString(),
                };
            }
            else if (parameter.DefaultValue is { } expression && ReportDefaultExpressions.TryEvaluate(expression, today, out var evaluated))
            {
                values[parameter.Name] = evaluated;
            }
        }

        return values;
    }

    private static ExportFormat ToExport(ReportOutputFormat format) => format switch
    {
        ReportOutputFormat.Excel => ExportFormat.Excel,
        ReportOutputFormat.Word => ExportFormat.Word,
        _ => ExportFormat.Pdf,
    };

    private static string Extension(ReportOutputFormat format) => format switch
    {
        ReportOutputFormat.Excel => ".xlsx",
        ReportOutputFormat.Word => ".docx",
        _ => ".pdf",
    };

    private static string ContentType(ReportOutputFormat format) => format switch
    {
        ReportOutputFormat.Excel => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ReportOutputFormat.Word => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/pdf",
    };

    private static string FileName(string name, ReportOutputFormat format)
    {
        var safe = new string(name.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_').Take(60).ToArray());
        if (safe.Length == 0)
        {
            safe = "report";
        }

        return safe + Extension(format);
    }
}
