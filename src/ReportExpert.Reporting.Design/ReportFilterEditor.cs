using System.Text.RegularExpressions;
using System.Xml.Linq;
using ReportExpert.Rdl.Core;

namespace ReportExpert.Reporting.Design;

/// <summary>
/// Edits RDL <c>Filters</c> on the report dataset. Filters run inside the renderer after the
/// provider has returned rows the user may see, so a filter can only narrow that scope.
/// </summary>
internal static partial class ReportFilterEditor
{
    public const int MaxFilters = 20;

    public static readonly string[] Operators = ["Equal", "NotEqual", "GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual", "Like"];

    /// <summary>
    /// Adds a filter. <paramref name="field"/> is a field of the first dataset, or <c>DataSet/Field</c>
    /// to filter another dataset.
    /// </summary>
    public static void Add(RdlDocument document, string field, string operation, string value, bool skipWhenBlank)
    {
        var ns = document.Ns;
        var slash = field.IndexOf('/', StringComparison.Ordinal);
        var dataSetName = slash < 0 ? null : field[..slash];
        field = slash < 0 ? field : field[(slash + 1)..];
        var dataset = DataSet(document, dataSetName) ?? throw new ReportDesignException(ReportCodes.NeedDataSet);
        var fields = dataset.Element(ns + "Fields")?.Elements(ns + "Field")
            .Select(element => element.Attribute("Name")?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal) ?? [];
        if (!fields.Contains(field) || !Operators.Contains(operation, StringComparer.Ordinal))
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        var text = value.Trim();
        if (text.Length is 0 or > 500)
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        var parameters = document.Descendants("ReportParameter")
            .Select(parameter => parameter.Attribute("Name")?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var reference = ParameterReference().Match(text);
        if (reference.Success && !parameters.Contains(reference.Groups["name"].Value))
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        if (reference.Success && FeedsParameter(document, dataset.Attribute("Name")?.Value ?? "", reference.Groups["name"].Value))
        {
            // The parameter takes its values from this dataset, so filtering the dataset on it would loop.
            throw new ReportDesignException(ReportCodes.ParameterCycle);
        }

        var expression = "=Fields!" + field + ".Value";
        var filterValue = text;
        if (skipWhenBlank)
        {
            if (!reference.Success || operation is not ("Equal" or "Like"))
            {
                throw new ReportDesignException(ReportCodes.InvalidItem);
            }

            // When the parameter is blank both sides become "*", so every row passes.
            var name = reference.Groups["name"].Value;
            var blank = "Len(CStr(Parameters!" + name + ".Value & \"\")) = 0";
            expression = "=IIf(" + blank + ", \"*\", CStr(Fields!" + field + ".Value))";
            filterValue = "=IIf(" + blank + ", \"*\", CStr(Parameters!" + name + ".Value))";
        }

        var filters = dataset.Element(ns + "Filters");
        if (filters is null)
        {
            filters = new XElement(ns + "Filters");
            var anchor = dataset.Element(ns + "Fields") ?? dataset.Element(ns + "Query");
            if (anchor is null)
            {
                dataset.Add(filters);
            }
            else
            {
                anchor.AddAfterSelf(filters);
            }
        }

        if (filters.Elements(ns + "Filter").Count() >= MaxFilters)
        {
            throw new ReportDesignException(ReportCodes.Limit);
        }

        filters.Add(new XElement(
            ns + "Filter",
            new XElement(ns + "FilterExpression", expression),
            new XElement(ns + "Operator", operation),
            new XElement(ns + "FilterValues", new XElement(ns + "FilterValue", filterValue))));
    }

    public static void Remove(RdlDocument document, int index, string? dataSetName = null)
    {
        var ns = document.Ns;
        var filters = DataSet(document, dataSetName)?.Element(ns + "Filters");
        var items = filters?.Elements(ns + "Filter").ToArray() ?? [];
        if (index < 0 || index >= items.Length)
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        items[index].Remove();
        if (!filters!.Elements(ns + "Filter").Any())
        {
            filters.Remove();
        }
    }

    public static IReadOnlyList<DesignFilter> Read(XElement? dataset, XNamespace ns)
    {
        var filters = dataset?.Element(ns + "Filters")?.Elements(ns + "Filter").ToArray() ?? [];
        var result = new List<DesignFilter>(filters.Length);
        for (var index = 0; index < filters.Length; index++)
        {
            var filter = filters[index];
            result.Add(new DesignFilter(
                index,
                filter.Element(ns + "FilterExpression")?.Value ?? "",
                filter.Element(ns + "Operator")?.Value ?? "Equal",
                string.Join(", ", filter.Element(ns + "FilterValues")?.Elements(ns + "FilterValue").Select(item => item.Value) ?? [])));
        }

        return result;
    }

    private static XElement? DataSet(RdlDocument document, string? name) =>
        string.IsNullOrEmpty(name)
            ? document.Descendants("DataSet").FirstOrDefault()
            : document.Descendants("DataSet").FirstOrDefault(candidate => string.Equals(candidate.Attribute("Name")?.Value, name, StringComparison.Ordinal));

    [GeneratedRegex(@"^=Parameters!(?<name>[A-Za-z][A-Za-z0-9_]{0,40})\.Value$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterReference();

    /// <summary>True when a parameter's available or default values come from the dataset.</summary>
    internal static bool FeedsParameter(RdlDocument document, string dataSetName, string parameterName) =>
        document.Descendants("ReportParameter")
            .Where(parameter => string.Equals(parameter.Attribute("Name")?.Value, parameterName, StringComparison.Ordinal))
            .SelectMany(parameter => parameter.Descendants(document.Ns + "DataSetName"))
            .Any(name => string.Equals(name.Value, dataSetName, StringComparison.Ordinal));

    /// <summary>True when a dataset filter reads the parameter.</summary>
    internal static bool ReadsParameter(RdlDocument document, string dataSetName, string parameterName)
    {
        var dataset = DataSet(document, dataSetName);
        var token = "Parameters!" + parameterName + ".";
        return dataset?.Element(document.Ns + "Filters")?.Descendants()
            .Any(element => !element.HasElements && element.Value.Contains(token, StringComparison.OrdinalIgnoreCase)) ?? false;
    }
}
