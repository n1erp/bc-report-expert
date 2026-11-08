namespace ReportExpert.Reporting.Design;

/// <summary>
/// How a dataset names its data source in <c>Query/CommandText</c>.
/// </summary>
/// <remarks>
/// <para>
/// REX providers are written as <c>rex:{key}</c>. Reports designed in SafiPay use <c>safipay:{key}</c>; REX reads
/// that form too, so a SafiPay report opens without changes.
/// </para>
/// <para>
/// A Business Central layout leaves <c>CommandText</c> empty (the AL report object supplies the rows at run time),
/// and other RDLC files may carry a SQL statement or a stored procedure name. Both are served by the
/// Business Central provider (<see cref="BusinessCentralKey"/>) and are never rewritten, so the layout stays
/// valid for BC.
/// </para>
/// </remarks>
public static class ReportDataCommand
{
    public const string Prefix = "rex:";

    public const string LegacyPrefix = "safipay:";

    /// <summary>The provider that serves datasets declared by the layout itself.</summary>
    public const string BusinessCentralKey = "bc";

    /// <summary>The data source name Business Central writes into generated layouts.</summary>
    public const string BusinessCentralDataSource = "DataSource";

    /// <summary>The command text written for a provider key. Business Central datasets keep an empty command.</summary>
    public static string For(string key) =>
        key.Equals(BusinessCentralKey, StringComparison.OrdinalIgnoreCase) ? "" : Prefix + key;

    /// <summary>True when the command names a REX (or SafiPay) provider explicitly.</summary>
    public static bool IsProviderCommand(string? command)
    {
        var text = command?.Trim() ?? "";
        return text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            || text.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The provider key a dataset reads. Any command that is not a provider command reads Business Central data.</summary>
    public static string ProviderKey(string? command)
    {
        var text = command?.Trim() ?? "";
        if (text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return text[Prefix.Length..];
        }

        if (text.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return text[LegacyPrefix.Length..];
        }

        return BusinessCentralKey;
    }
}
