using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReportExpert.Rdl.Core;
using ReportExpert.Reporting.Design;

namespace ReportExpert.Rdl.Mcp.Server;

/// <summary>
/// Runs the REX V3 report design engine (<see cref="IReportDesignService"/>) behind the MCP tools that
/// work on a layout as a designer does: place items, set properties, arrange, group, preview data and export.
/// </summary>
/// <remarks>
/// The engine keeps its own revision history in the REX workspace. A tool call opens the file (importing it
/// the first time), applies one change, and writes the file back. A dry run applies the change, reports the
/// diff, and steps the workspace back so neither the file nor the history keeps it.
/// </remarks>
internal static class DesignSession
{
    private static readonly Lazy<ServiceProvider> Services = new(() => new ServiceCollection()
        .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
        .AddReportDesign(options => options.StoragePath = Environment.GetEnvironmentVariable("REX_DESIGN_WORKSPACE"))
        .BuildServiceProvider());

    /// <summary>The shared design service.</summary>
    public static IReportDesignService Service => Services.Value.GetRequiredService<IReportDesignService>();

    /// <summary>Opens a report through the engine and returns a value read from its design model.</summary>
    public static ToolResponse Read<T>(string filePath, Func<IReportDesignService, ReportDesignModel, Task<T>> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        return Guard(() =>
        {
            string resolved = ToolGuards.ResolvePath(filePath);
            var model = Service.OpenFileAsync(resolved, CancellationToken.None).GetAwaiter().GetResult();
            return ToolResponse.Success(read(Service, model).GetAwaiter().GetResult());
        });
    }

    /// <summary>Applies one design change and writes the layout back, or reports it as a diff on a dry run.</summary>
    public static ToolResponse Mutate(
        string filePath,
        bool dryRun,
        Func<IReportDesignService, ReportDesignModel, Task<ReportDesignModel>> change,
        string message)
    {
        ArgumentNullException.ThrowIfNull(change);

        return Guard(() =>
        {
            string resolved = ToolGuards.ResolvePath(filePath);
            var service = Service;
            var opened = service.OpenFileAsync(resolved, CancellationToken.None).GetAwaiter().GetResult();
            var changed = change(service, opened).GetAwaiter().GetResult();

            var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["success"] = true,
                ["message"] = message,
                ["dry_run"] = dryRun,
                ["revision"] = changed.Revision,
                ["selected_item"] = changed.SelectedItem,
            };

            if (dryRun)
            {
                string before = File.ReadAllText(resolved);
                var output = service.DefinitionAsync(changed.Id, CancellationToken.None).GetAwaiter().GetResult();
                string after = System.Text.Encoding.UTF8.GetString(output.Content);
                if (changed.Revision > opened.Revision)
                {
                    service.StepAsync(changed.Id, -1, CancellationToken.None).GetAwaiter().GetResult();
                }

                payload["diff"] = UnifiedDiff.Create(before, after, Path.GetFileName(resolved));
                payload["message"] = $"[dry run, nothing written] {message}";
                return ToolResponse.Success(payload);
            }

            if (ServerOptions.BackupsEnabled)
            {
                payload["backup_path"] = BackupStore.Create(resolved);
            }

            service.SaveToFileAsync(changed.Id, null, CancellationToken.None).GetAwaiter().GetResult();
            return ToolResponse.Success(payload);
        });
    }

    /// <summary>
    /// Runs an engine call through <see cref="ToolGuards.Run{T}"/> so file and argument errors get the usual
    /// envelope. A rejection from the engine becomes a <c>refused</c> or <c>not_found</c> failure with its text.
    /// </summary>
    private static ToolResponse Guard(Func<ToolResponse> operation)
    {
        var wrapped = ToolGuards.Run(() =>
        {
            try
            {
                return operation();
            }
            catch (ReportDesignException exception) when (exception.Message == ReportCodes.NotFound)
            {
                throw new FileNotFoundException(ReportMessages.For(exception));
            }
            catch (ReportDesignException exception)
            {
                throw new RdlRefusedException(
                    ReportMessages.For(exception),
                    "Check the item, dataset and field names with get_rdl_design_model, then try again.");
            }
        });

        return wrapped.Ok && wrapped.Data is ToolResponse inner ? inner : wrapped;
    }
}
