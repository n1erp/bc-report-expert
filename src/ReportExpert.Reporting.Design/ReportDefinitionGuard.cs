using System.Text.RegularExpressions;
using System.Xml.Linq;
using ReportExpert.Rdl.Core;

namespace ReportExpert.Reporting.Design;

internal static partial class ReportDefinitionGuard
{
    private static readonly string[] RejectedElements =
    [
        "Code", "Class", "Classes", "Subreport", "SharedDataSet", "CustomReportItem",
    ];

    private static readonly string[] BannedFragments =
    [
        "system.", "microsoft.", ".gettype", "gettype(", "assembly.", "process.", "typeof(",
        "environment.", "reflection", "activator.", "createobject", "eval(", "execute(",
        "callbyname", "http:", "https:", "file:", "sqlclient",
    ];

    public static void EnsureSafe(RdlDocument document, IReadOnlySet<string> providerKeys, ReportingOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        var xml = document.Serialize();
        if (xml.Length > options.MaxDefinitionBytes)
        {
            throw new ReportDesignException(ReportCodes.Limit);
        }

        if (options.Policy == ReportSafetyPolicy.Trusted)
        {
            EnsureWithinLimits(document, options);
            return;
        }

        var count = 0;
        foreach (var element in document.Root.DescendantsAndSelf())
        {
            count++;
            if (count > 20_000)
            {
                throw new ReportDesignException(ReportCodes.Limit);
            }

            if (RejectedElements.Contains(element.Name.LocalName, StringComparer.Ordinal))
            {
                throw new ReportDesignException(ReportCodes.UnsafeDefinition);
            }
        }

        var items = document.Root.Descendants().Count(element =>
            element.Parent?.Name.LocalName == "ReportItems"
            && element.Attribute("Name") is not null);
        if (items > options.MaxItems)
        {
            throw new ReportDesignException(ReportCodes.Limit);
        }

        foreach (var source in document.Descendants("Source"))
        {
            var kind = source.Value.Trim();
            if (!kind.Equals("Embedded", StringComparison.OrdinalIgnoreCase))
            {
                throw new ReportDesignException(ReportCodes.UnsafeDefinition);
            }
        }

        foreach (var image in document.Descendants("EmbeddedImage"))
        {
            var mime = image.Element(document.Ns + "MIMEType")?.Value ?? "";
            if (!mime.Equals("image/png", StringComparison.OrdinalIgnoreCase)
                && !mime.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase))
            {
                throw new ReportDesignException(ReportCodes.UnsafeDefinition);
            }

            var data = image.Element(document.Ns + "ImageData")?.Value ?? "";
            if (data.Length > 350_000)
            {
                throw new ReportDesignException(ReportCodes.Limit);
            }
        }

        var datasets = document.Descendants("DataSet").ToArray();
        if (datasets.Length > 8)
        {
            throw new ReportDesignException(ReportCodes.Limit);
        }

        foreach (var dataset in datasets)
        {
            var command = dataset.Element(document.Ns + "Query")?.Element(document.Ns + "CommandText")?.Value?.Trim() ?? "";
            if (!ReportDataCommand.IsProviderCommand(command))
            {
                throw new ReportDesignException(ReportCodes.UnsafeDefinition);
            }

            var key = ReportDataCommand.ProviderKey(command);
            if (!providerKeys.Contains(key))
            {
                throw new ReportDesignException(ReportCodes.UnknownProvider);
            }
        }

        if (document.Descendants("ReportParameter").Count() > 30)
        {
            throw new ReportDesignException(ReportCodes.Limit);
        }

        foreach (var element in document.Root.DescendantsAndSelf())
        {
            var text = element.Nodes().OfType<XText>().FirstOrDefault()?.Value;
            if (string.IsNullOrEmpty(text) || !text.TrimStart().StartsWith('='))
            {
                continue;
            }

            if (text.Length > 2000 || !ExpressionAllowed(text))
            {
                throw new ReportDesignException(ReportCodes.UnsafeDefinition);
            }
        }
    }

    /// <summary>
    /// The checks the desktop keeps for its own files: a definition that is too large or too deep to edit
    /// interactively is refused, and every dataset must name a data source REX can supply. A Business Central
    /// dataset (empty or foreign command) is always served by the Business Central provider.
    /// </summary>
    private static void EnsureWithinLimits(RdlDocument document, ReportingOptions options)
    {
        var items = document.Root.Descendants().Count(element =>
            element.Parent?.Name.LocalName == "ReportItems"
            && element.Attribute("Name") is not null);
        if (items > options.MaxItems)
        {
            throw new ReportDesignException(ReportCodes.Limit);
        }

        if (document.Descendants("DataSet").Count() > 64 || document.Descendants("ReportParameter").Count() > 500)
        {
            throw new ReportDesignException(ReportCodes.Limit);
        }
    }

    /// <summary>
    /// Collections the RDL schema requires to hold at least one child. The designer keeps them empty while a
    /// report has no dataset or no item (a new report, or after the last one is removed), but ReportViewer
    /// refuses such a definition with "has incomplete content".
    /// </summary>
    private static readonly string[] NonEmptyCollections =
    [
        "DataSets", "DataSources", "ReportItems", "ReportParameters", "EmbeddedImages", "Fields", "Filters",
        "SortExpressions", "GroupExpressions", "Variables", "CustomProperties", "QueryParameters", "ParameterValues",
        "CellDefinitions",
    ];

    /// <summary>
    /// Removes empty schema collections from a definition about to be rendered or downloaded. Removing one can
    /// empty its parent (a rectangle whose last item went), so this repeats until nothing changes.
    /// The stored definition keeps its empty collections, which the editors add to.
    /// </summary>
    public static void RemoveEmptyCollections(RdlDocument document)
    {
        while (true)
        {
            var empty = document.Root.Descendants()
                .Where(element => NonEmptyCollections.Contains(element.Name.LocalName, StringComparer.Ordinal) && !element.HasElements)
                .ToArray();
            if (empty.Length == 0)
            {
                return;
            }

            foreach (var element in empty)
            {
                element.Remove();
            }
        }
    }

    public static void NormalizeDataSources(RdlDocument document)
    {
        foreach (var connection in document.Descendants("ConnectionProperties"))
        {
            Set(connection, document.Ns, "DataProvider", "System.Data.DataSet");
            Set(connection, document.Ns, "ConnectString", "/* Report Expert */");
            connection.Element(document.Ns + "Prompt")?.Remove();
            connection.Element(document.Ns + "IntegratedSecurity")?.Remove();
        }
    }

    public static string RequireName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length is < 1 or > 80 || trimmed.Any(char.IsControl) || trimmed.IndexOfAny(['/', '\\', ':']) >= 0)
        {
            throw new ReportDesignException(ReportCodes.InvalidName);
        }

        return trimmed;
    }

    public static bool ValidIdentifier(string? name) =>
        !string.IsNullOrWhiteSpace(name) && IdentifierPattern().IsMatch(name);

    private static bool ExpressionAllowed(string expression)
    {
        var comparable = expression.ToLowerInvariant();
        if (BannedFragments.Any(comparable.Contains) || CustomCode().IsMatch(expression))
        {
            return false;
        }

        if (NewExpression().IsMatch(expression))
        {
            return false;
        }

        var withoutMe = MeValue().Replace(expression, "");
        return !withoutMe.Contains("Me.", StringComparison.OrdinalIgnoreCase);
    }

    private static void Set(XElement parent, XNamespace ns, string name, string value)
    {
        var child = parent.Element(ns + name);
        if (child is null)
        {
            child = new XElement(ns + name);
            parent.Add(child);
        }

        child.Value = value;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"(?<![!\w])code\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CustomCode();

    [GeneratedRegex(@"\bnew\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NewExpression();

    [GeneratedRegex(@"Me\.Value", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MeValue();
}
