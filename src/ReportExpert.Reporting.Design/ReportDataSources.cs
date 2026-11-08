using System.Data;
using System.Globalization;
using System.Text;

namespace ReportExpert.Reporting.Design;

/// <summary>A provider that can return only the fields a report dataset declares.</summary>
public interface IReportFieldReader
{
    Task<DataTable> ReadAsync(IReadOnlyList<string> fields, IReadOnlyDictionary<string, string?> parameters, int maxRows, CancellationToken cancellationToken);
}

/// <summary>Display metadata for a provider, used to group the data panel.</summary>
public interface IReportDataSourceInfo
{
    /// <summary>Group shown in the data panel: <c>REX</c> for built-in providers, <c>Business Central</c> for BC datasets.</summary>
    string Group { get; }

    /// <summary>Label shown in the data panel, for example <c>hr.employee</c>.</summary>
    string Label { get; }

    bool IsView { get; }
}

/// <summary>
/// Every data source a report may use: the providers registered with dependency injection. On the desktop
/// these are the Business Central dataset provider (<see cref="BusinessCentralDataProvider"/>), which serves
/// the datasets declared in the RDLC itself, and any provider a host or plug-in registers.
/// </summary>
public sealed class ReportDataSources(IEnumerable<IReportDataProvider> providers)
{
    private IReadOnlyList<IReportDataProvider>? loaded;

    public Task<IReadOnlyList<IReportDataProvider>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        loaded ??= providers
            .GroupBy(provider => provider.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        return Task.FromResult(loaded);
    }

    public async Task<IReadOnlySet<string>> KeysAsync(CancellationToken cancellationToken) =>
        (await ListAsync(cancellationToken)).Select(provider => provider.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The provider for a key. A key REX has no provider for (for example a SafiPay <c>safipay:src.*</c> source,
    /// which needs the SafiPay database) falls back to the Business Central provider, so the layout still
    /// previews and exports from its declared fields: an attached or sibling data file, or sample rows.
    /// </summary>
    public async Task<IReportDataProvider?> FindAsync(string key, CancellationToken cancellationToken)
    {
        var all = await ListAsync(cancellationToken);
        return all.FirstOrDefault(provider => provider.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(provider => provider.Key.Equals(ReportDataCommand.BusinessCentralKey, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads rows for a dataset. A provider that knows datasets by name (<see cref="IReportDataSetReader"/>)
    /// gets the dataset and its declared fields; one that can narrow fields gets the fields.
    /// </summary>
    public static Task<DataTable> ReadAsync(
        IReportDataProvider provider,
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, string?> parameters,
        int maxRows,
        CancellationToken cancellationToken,
        ReportDataSetRequest? request = null) =>
        provider switch
        {
            IReportDataSetReader named when request is not null => named.ReadAsync(request, parameters, maxRows, cancellationToken),
            IReportFieldReader reader when fields.Count > 0 => reader.ReadAsync(fields, parameters, maxRows, cancellationToken),
            _ => provider.ReadAsync(parameters, maxRows, cancellationToken),
        };
}

/// <summary>The dataset a renderer or the data panel needs rows for.</summary>
/// <param name="ReportId">The report being rendered.</param>
/// <param name="DataSetName">The dataset name in the definition, for example <c>DataSet_Result</c>.</param>
/// <param name="Fields">The fields the dataset declares, with their <c>rd:TypeName</c>.</param>
public sealed record ReportDataSetRequest(Guid ReportId, string DataSetName, IReadOnlyList<ReportField> Fields);

/// <summary>A provider that serves datasets by name, such as the Business Central provider.</summary>
public interface IReportDataSetReader
{
    Task<DataTable> ReadAsync(ReportDataSetRequest request, IReadOnlyDictionary<string, string?> parameters, int maxRows, CancellationToken cancellationToken);
}

public interface IReportSourceProvider
{
    IReadOnlyList<string> Filters { get; }
}
