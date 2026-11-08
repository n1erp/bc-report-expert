using System.Data;

namespace ReportExpert.Reporting.Design;

/// <summary>
/// A compiled data source the designer can bind. Implementations supply the rows for one data source.
/// </summary>
/// <remarks>
/// <para>
/// Register an implementation with dependency injection, for example
/// <c>services.AddScoped&lt;IReportDataProvider, MyProvider&gt;()</c> in the composition root.
/// The designer discovers every registration. <see cref="Key"/> becomes the report command
/// <c>rex:{key}</c>. The report definition never contains a connection string or a SQL statement.
/// </para>
/// <para>
/// Do not load a provider from an uploaded assembly. A new provider is a reviewed project reference
/// deployed with the application. Running untrusted code needs a separate process and an allow-list;
/// that host is not part of this application.
/// </para>
/// </remarks>
public interface IReportDataProvider
{
    /// <summary>Stable identifier, lowercase letters, digits, and hyphens.</summary>
    string Key { get; }

    /// <summary>Dataset name written into the report definition.</summary>
    string DataSetName { get; }

    /// <summary>UI resource key for the provider label.</summary>
    string ResourceKey { get; }

    IReadOnlyList<ReportField> Fields { get; }

    /// <summary>
    /// Reads rows the signed-in user is allowed to see. <paramref name="parameters"/> are the
    /// report parameters; they are not a query language.
    /// </summary>
    Task<DataTable> ReadAsync(IReadOnlyDictionary<string, string?> parameters, int maxRows, CancellationToken cancellationToken);
}
