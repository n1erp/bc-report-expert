namespace ReportExpert.Reporting.Design;

/// <summary>How strictly a report definition is checked before it is stored or rendered.</summary>
public enum ReportSafetyPolicy
{
    /// <summary>
    /// The desktop default. The user owns the files, so Business Central layouts keep their <c>Code</c> block,
    /// subreports and any query text. Only size limits apply.
    /// </summary>
    Trusted,

    /// <summary>
    /// The SafiPay web policy: embedded data sources only, provider commands only, no custom code and a
    /// restricted expression language. Use it for definitions that come from someone else.
    /// </summary>
    Strict,
}

/// <summary>Caps, policy and storage location for the report designer.</summary>
public sealed class ReportingOptions
{
    public ReportSafetyPolicy Policy { get; set; } = ReportSafetyPolicy.Trusted;

    public const string SectionName = "ReportExpert:Reporting";

    /// <summary>
    /// Directory that holds the report workspace (revisions, clipboard, audit log). When empty, REX uses
    /// <c>%LocalAppData%\ReportExpert\reports</c>.
    /// </summary>
    public string? StoragePath { get; set; }

    public int MaxRows { get; set; } = 100_000;

    /// <summary>
    /// Rows the Business Central dataset generates per dataset when no data file is attached (Settings ›
    /// Preview › Sample rows). A data file is always read in full, up to <see cref="MaxRows"/>.
    /// </summary>
    public int SampleRows { get; set; } = DefaultSampleRows;

    /// <summary>The sample row count when nothing is configured.</summary>
    public const int DefaultSampleRows = 25;

    /// <summary>The largest sample row count Settings allows.</summary>
    public const int MaxSampleRows = 500;

    /// <summary>
    /// Row cap for curated sources (pay lines, declarations). They are already narrowed by period and company
    /// in SQL, and a monthly journal for a large company has more than <see cref="MaxRows"/> lines.
    /// </summary>
    public int MaxSourceRows { get; set; } = 200_000;

    public int MaxDefinitionBytes { get; set; } = 32 * 1024 * 1024;

    public int MaxExportBytes { get; set; } = 256 * 1024 * 1024;

    public int ExecutionTimeoutSeconds { get; set; } = 120;

    public int MaxItems { get; set; } = 5000;

    public int MaxVersions { get; set; } = 50;
}
