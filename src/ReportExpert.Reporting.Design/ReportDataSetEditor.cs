using System.Globalization;
using System.Xml.Linq;
using ReportExpert.Rdl.Core;
using ReportExpert.Rdl.Core.Editing;

namespace ReportExpert.Reporting.Design;

/// <summary>Adds, rebinds and removes report datasets. Each dataset reads one data source through <c>rex:{key}</c>, or the layout's own Business Central dataset.</summary>
internal static class ReportDataSetEditor
{
    public const string DataSourceName = ReportDataCommand.BusinessCentralDataSource;

    public const int MaxDataSets = 8;

    /// <summary>Adds a dataset over <paramref name="provider"/> with the chosen fields and returns its name.</summary>
    public static string Add(RdlDocument document, IReportDataProvider provider, string? name, IReadOnlyList<string> fields)
    {
        var existing = Names(document);
        if (existing.Count >= MaxDataSets)
        {
            throw new ReportDesignException(ReportCodes.Limit);
        }

        var chosen = Choose(provider, fields);
        var dataSetName = UniqueName(existing, string.IsNullOrWhiteSpace(name) ? provider.DataSetName : name.Trim());
        EnsureDataSource(document);
        DataSetEditor.AddDataSet(document, dataSetName, ReportDataCommand.For(provider.Key), "Text", DataSourceName);
        foreach (var field in chosen)
        {
            DataSetEditor.AddField(document, dataSetName, field.Name, field.Name, field.DataType);
        }

        return dataSetName;
    }

    /// <summary>Replaces the fields of an existing dataset, keeping its filters and references.</summary>
    public static void SetFields(RdlDocument document, string dataSetName, IReportDataProvider provider, IReadOnlyList<string> fields)
    {
        var dataset = Require(document, dataSetName);
        var ns = document.Ns;
        var chosen = Choose(provider, fields);
        var container = dataset.Element(ns + "Fields");
        if (container is null)
        {
            container = new XElement(ns + "Fields");
            dataset.Element(ns + "Query")?.AddAfterSelf(container);
            if (container.Parent is null)
            {
                dataset.Add(container);
            }
        }

        container.RemoveNodes();
        foreach (var field in chosen)
        {
            DataSetEditor.AddField(document, dataSetName, field.Name, field.Name, field.DataType);
        }
    }

    /// <summary>Removes a dataset no data region, chart or parameter uses.</summary>
    public static void Remove(RdlDocument document, string dataSetName)
    {
        Require(document, dataSetName);
        var used = document.Root.Descendants()
            .Where(element => element.Name.LocalName == "DataSetName" && element.Parent?.Name.LocalName != "DataSet")
            .Any(element => string.Equals(element.Value.Trim(), dataSetName, StringComparison.Ordinal));
        if (used)
        {
            throw new ReportDesignException(ReportCodes.DataSetInUse);
        }

        DataSetEditor.RemoveDataSet(document, dataSetName, force: true);
    }

    /// <summary>Fields declared on a dataset, with their CLR type names.</summary>
    public static IReadOnlyList<ReportField> Fields(RdlDocument document, string? dataSetName)
    {
        var ns = document.Ns;
        var dataset = string.IsNullOrEmpty(dataSetName)
            ? document.Descendants("DataSet").FirstOrDefault()
            : document.Descendants("DataSet").FirstOrDefault(candidate => string.Equals(candidate.Attribute("Name")?.Value, dataSetName, StringComparison.Ordinal));
        return dataset?.Element(ns + "Fields")?.Elements(ns + "Field")
            .Select(field => new ReportField(
                field.Attribute("Name")?.Value ?? "",
                field.Element(RdlDocument.Rd + "TypeName")?.Value ?? "System.String"))
            .Where(field => field.Name.Length > 0)
            .ToArray() ?? [];
    }

    public static IReadOnlyList<string> Names(RdlDocument document) =>
        document.Descendants("DataSet")
            .Select(dataset => dataset.Attribute("Name")?.Value)
            .OfType<string>()
            .ToArray();

    /// <summary>The provider key a dataset reads. Datasets without a REX command read Business Central data.</summary>
    public static string? ProviderKey(RdlDocument document, string dataSetName)
    {
        var command = Require(document, dataSetName).Element(document.Ns + "Query")?.Element(document.Ns + "CommandText")?.Value?.Trim();
        return ReportDataCommand.ProviderKey(command);
    }

    /// <summary>Points a data region or chart at another dataset.</summary>
    public static void Bind(RdlDocument document, string itemName, string dataSetName)
    {
        Require(document, dataSetName);
        var item = document.Root.Descendants()
            .FirstOrDefault(element => element.Name.LocalName is "Tablix" or "Chart"
                && string.Equals(element.Attribute("Name")?.Value, itemName, StringComparison.Ordinal))
            ?? throw new ReportDesignException(ReportCodes.InvalidItem);
        var reference = item.Element(document.Ns + "DataSetName");
        if (reference is null)
        {
            item.Add(new XElement(document.Ns + "DataSetName", dataSetName));
        }
        else
        {
            reference.Value = dataSetName;
        }
    }

    private static List<ReportField> Choose(IReportDataProvider provider, IReadOnlyList<string> fields)
    {
        var chosen = fields.Count == 0
            ? provider.Fields.ToList()
            : provider.Fields.Where(field => fields.Contains(field.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (chosen.Count == 0)
        {
            throw new ReportDesignException(ReportCodes.NeedFields);
        }

        return chosen;
    }

    private static string UniqueName(IReadOnlyList<string> existing, string requested)
    {
        var baseName = new string(requested.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_').ToArray());
        if (baseName.Length == 0 || !char.IsAsciiLetter(baseName[0]))
        {
            baseName = "DataSet" + baseName;
        }

        if (baseName.Length > 36)
        {
            baseName = baseName[..36];
        }

        var name = baseName;
        for (var index = 2; existing.Contains(name, StringComparer.OrdinalIgnoreCase); index++)
        {
            name = baseName + index.ToString(CultureInfo.InvariantCulture);
        }

        return name;
    }

    private static void EnsureDataSource(RdlDocument document)
    {
        var ns = document.Ns;
        if (document.Descendants("DataSource").Any(source => string.Equals(source.Attribute("Name")?.Value, DataSourceName, StringComparison.Ordinal)))
        {
            return;
        }

        var sources = document.Root.Element(ns + "DataSources");
        if (sources is null)
        {
            sources = new XElement(ns + "DataSources");
            document.Root.AddFirst(sources);
        }

        sources.Add(new XElement(
            ns + "DataSource",
            new XAttribute("Name", DataSourceName),
            new XElement(
                ns + "ConnectionProperties",
                new XElement(ns + "DataProvider", "System.Data.DataSet"),
                new XElement(ns + "ConnectString", "/* Report Expert */"))));
    }

    private static XElement Require(RdlDocument document, string dataSetName) =>
        document.Descendants("DataSet").FirstOrDefault(candidate => string.Equals(candidate.Attribute("Name")?.Value, dataSetName, StringComparison.Ordinal))
        ?? throw new ReportDesignException(ReportCodes.InvalidItem);
}
