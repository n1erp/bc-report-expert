using System.Globalization;
using System.Xml.Linq;
using ReportExpert.Rdl.Core;

namespace ReportExpert.Reporting.Design;

/// <summary>
/// Reads and edits the parts of a <c>ReportParameter</c> the designer exposes: type, prompt,
/// literal or expression default, static available values, and the blank, null and hidden flags.
/// Parts the designer does not edit (a dataset-driven default or value list, <c>MultiValue</c>,
/// <c>UsedInQuery</c>) are kept as they are.
/// </summary>
internal static class ReportParameterEditor
{
    public const int MaxOptions = 100;

    public static readonly string[] Types = ["String", "Integer", "Float", "DateTime", "Boolean"];

    private static readonly string[] Managed = ["DataType", "Nullable", "DefaultValue", "AllowBlank", "Prompt", "Hidden", "ValidValues", "MultiValue"];

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss",
        "dd/MM/yyyy", "dd/MM/yyyy HH:mm",
    ];

    public static IReadOnlyList<DesignParameter> ReadAll(RdlDocument document)
    {
        var ns = document.Ns;
        return document.Descendants("ReportParameter")
            .Select(parameter => Read(parameter, ns))
            .ToArray();
    }

    /// <summary>Separates the values of a multi-value parameter in the raw text passed to <see cref="Resolve"/>.</summary>
    public const char ValueSeparator = ReportParameterValues.Separator;

    public static void Edit(RdlDocument document, ReportParameterEdit edit)
    {
        var ns = document.Ns;
        var parameter = document.Descendants("ReportParameter")
            .FirstOrDefault(candidate => string.Equals(candidate.Attribute("Name")?.Value, edit.Name, StringComparison.Ordinal))
            ?? throw new ReportDesignException(ReportCodes.InvalidItem);

        var type = Types.Contains(edit.DataType ?? "", StringComparer.Ordinal) ? edit.DataType! : "String";
        var multiValue = edit.MultiValue && type != "Boolean";
        var prompt = Clip(edit.Prompt, 80);
        if (prompt.Length == 0)
        {
            prompt = edit.Name;
        }

        var existingValues = parameter.Element(ns + "ValidValues");
        var existingDefault = parameter.Element(ns + "DefaultValue");

        // Available values: None, Specify (a static list) or Query (a dataset field).
        var availableSource = edit.AvailableSource
            ?? (!string.IsNullOrWhiteSpace(edit.Options) ? ParameterValueSource.Specify
                : existingValues?.Element(ns + "DataSetReference") is not null ? "Keep" : ParameterValueSource.None);
        var options = availableSource == ParameterValueSource.Specify ? ParseOptions(edit.Options, type) : [];
        XElement? validValues = availableSource switch
        {
            "Keep" => existingValues,
            ParameterValueSource.Specify when options.Count > 0 => new XElement(
                ns + "ValidValues",
                new XElement(
                    ns + "ParameterValues",
                    options.Select(option => new XElement(
                        ns + "ParameterValue",
                        new XElement(ns + "Value", option.Value),
                        new XElement(ns + "Label", option.Label))))),
            ParameterValueSource.Query => new XElement(ns + "ValidValues", DataSetReference(document, edit.Name, edit.OptionsDataSet, edit.OptionsValueField, edit.OptionsLabelField)),
            _ => null,
        };

        // Default values: None, Specify (literal or expression, one per line when multi-value) or Query.
        var defaultSource = edit.DefaultSource
            ?? (!string.IsNullOrWhiteSpace(edit.DefaultValue) ? ParameterValueSource.Specify
                : existingDefault?.Element(ns + "DataSetReference") is not null ? "Keep" : ParameterValueSource.None);
        var defaults = defaultSource == ParameterValueSource.Specify ? ParseDefaults(edit.DefaultValue, type, multiValue, options) : [];
        if (defaultSource == ParameterValueSource.Specify && defaults.Count == 0)
        {
            defaultSource = ParameterValueSource.None;
        }

        XElement? defaultElement = defaultSource switch
        {
            "Keep" => existingDefault,
            ParameterValueSource.Specify => new XElement(ns + "DefaultValue", new XElement(ns + "Values", defaults.Select(value => new XElement(ns + "Value", value)))),
            ParameterValueSource.Query => new XElement(ns + "DefaultValue", DataSetReference(document, edit.Name, edit.DefaultDataSet, edit.DefaultValueField, null)),
            _ => null,
        };

        var allowBlank = edit.AllowBlank && type == "String";
        if (edit.Hidden && defaultElement is null && !edit.Nullable && !allowBlank)
        {
            throw new ReportDesignException(ReportCodes.ParameterNeedsDefault);
        }

        var preserved = parameter.Elements()
            .Where(element => !Managed.Contains(element.Name.LocalName, StringComparer.Ordinal))
            .ToArray();
        foreach (var element in parameter.Elements().ToArray())
        {
            element.Remove();
        }

        parameter.Add(new XElement(ns + "DataType", type));
        if (edit.Nullable)
        {
            parameter.Add(new XElement(ns + "Nullable", "true"));
        }

        if (defaultElement is not null)
        {
            parameter.Add(defaultElement);
        }

        if (allowBlank)
        {
            parameter.Add(new XElement(ns + "AllowBlank", "true"));
        }

        parameter.Add(new XElement(ns + "Prompt", prompt));
        if (edit.Hidden)
        {
            parameter.Add(new XElement(ns + "Hidden", "true"));
        }

        if (validValues is not null)
        {
            parameter.Add(validValues);
        }

        if (multiValue)
        {
            parameter.Add(new XElement(ns + "MultiValue", "true"));
        }

        parameter.Add(preserved);
    }

    /// <summary>
    /// Turns the text a user typed for <paramref name="parameter"/> into the value sent to the renderer.
    /// A multi-value parameter receives its values joined with <see cref="ValueSeparator"/>.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the value is left out so the definition's default applies.
    /// Otherwise <paramref name="value"/> is the invariant text, a string array for a multi-value
    /// parameter, or <see langword="null"/> for null.
    /// </returns>
    public static bool Resolve(DesignParameter parameter, string? raw, out object? value)
    {
        var parts = (raw ?? "").Split(ValueSeparator)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToArray();
        if (parts.Length == 0)
        {
            if (parameter.DefaultDataSet is not null || parameter.DefaultValue is { } fallback && fallback.StartsWith('='))
            {
                value = null;
                return false;
            }

            if (parameter.DefaultValue is not null)
            {
                var defaults = parameter.DefaultValues ?? [parameter.DefaultValue];
                value = parameter.MultiValue ? defaults.ToArray() : (object)defaults[0];
                return true;
            }

            if (parameter.DataType == "String" && parameter.AllowBlank)
            {
                value = parameter.MultiValue ? new[] { "" } : (object)"";
                return true;
            }

            if (parameter.Nullable && !parameter.MultiValue)
            {
                value = null;
                return true;
            }

            throw new ReportDesignException(ReportCodes.ParameterRequired);
        }

        if (!parameter.MultiValue && parts.Length > 1)
        {
            throw new ReportDesignException(ReportCodes.InvalidParameterValue);
        }

        var options = parameter.Options ?? [];
        var normalized = new string[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            var current = Normalize(parts[index], parameter.DataType) ?? throw new ReportDesignException(ReportCodes.InvalidParameterValue);
            if (options.Count > 0 && !options.Any(option => string.Equals(option.Value, current, StringComparison.Ordinal)))
            {
                throw new ReportDesignException(ReportCodes.InvalidParameterValue);
            }

            normalized[index] = current;
        }

        value = parameter.MultiValue ? normalized : (object)normalized[0];
        return true;
    }

    private static XElement DataSetReference(RdlDocument document, string parameterName, string? dataSet, string? valueField, string? labelField)
    {
        var ns = document.Ns;
        if (string.IsNullOrWhiteSpace(dataSet) || string.IsNullOrWhiteSpace(valueField))
        {
            throw new ReportDesignException(ReportCodes.InvalidValue);
        }

        if (ReportFilterEditor.ReadsParameter(document, dataSet, parameterName))
        {
            throw new ReportDesignException(ReportCodes.ParameterCycle);
        }

        var fields = ReportDataSetEditor.Fields(document, dataSet).Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        if (fields.Count == 0 || !fields.Contains(valueField) || (!string.IsNullOrWhiteSpace(labelField) && !fields.Contains(labelField)))
        {
            throw new ReportDesignException(ReportCodes.InvalidValue);
        }

        var reference = new XElement(ns + "DataSetReference", new XElement(ns + "DataSetName", dataSet), new XElement(ns + "ValueField", valueField));
        if (!string.IsNullOrWhiteSpace(labelField))
        {
            reference.Add(new XElement(ns + "LabelField", labelField));
        }

        return reference;
    }

    private static List<string> ParseDefaults(string? text, string type, bool multiValue, IReadOnlyList<ParameterOption> options)
    {
        var lines = (text ?? "").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        if (!multiValue && lines.Count > 1)
        {
            lines = [string.Join(" ", lines)];
        }

        var values = new List<string>();
        foreach (var line in lines.Take(50))
        {
            if (line.StartsWith('='))
            {
                values.Add(Clip(line, 500));
                continue;
            }

            var normalized = Normalize(line, type) ?? throw new ReportDesignException(ReportCodes.InvalidParameterValue);
            if (options.Count > 0 && !options.Any(option => string.Equals(option.Value, normalized, StringComparison.Ordinal)))
            {
                throw new ReportDesignException(ReportCodes.InvalidParameterValue);
            }

            values.Add(normalized);
        }

        return values;
    }

    /// <summary>Normalizes a literal to the invariant form RDL expects, or returns null when it does not parse.</summary>
    public static string? Normalize(string text, string dataType)
    {
        switch (dataType)
        {
            case "Integer":
                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)
                    ? whole.ToString(CultureInfo.InvariantCulture)
                    : null;
            case "Float":
                var spaced = text.Replace(" ", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
                if (double.TryParse(spaced, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    || double.TryParse(spaced, NumberStyles.Float, CultureInfo.GetCultureInfo("fr-MA"), out number))
                {
                    return double.IsFinite(number) ? number.ToString("R", CultureInfo.InvariantCulture) : null;
                }

                return null;
            case "DateTime":
                if (!DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    return null;
                }

                return date.TimeOfDay == TimeSpan.Zero
                    ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            case "Boolean":
                return text.ToLowerInvariant() switch
                {
                    "true" or "1" or "yes" or "oui" => "true",
                    "false" or "0" or "no" or "non" => "false",
                    _ => null,
                };
            default:
                return text.Length > 200 ? text[..200] : text;
        }
    }

    private static DesignParameter Read(XElement parameter, XNamespace ns)
    {
        var defaultElement = parameter.Element(ns + "DefaultValue");
        var defaults = defaultElement?.Element(ns + "Values")?.Elements(ns + "Value").Where(value => !IsNil(value)).Select(value => value.Value).ToArray() ?? [];
        var defaultReference = defaultElement?.Element(ns + "DataSetReference");
        var validValues = parameter.Element(ns + "ValidValues");
        var optionsReference = validValues?.Element(ns + "DataSetReference");
        var options = validValues?.Element(ns + "ParameterValues")?.Elements(ns + "ParameterValue")
            .Select(option =>
            {
                var value = option.Element(ns + "Value")?.Value ?? "";
                var label = option.Element(ns + "Label")?.Value;
                return new ParameterOption(value, string.IsNullOrEmpty(label) ? value : label);
            })
            .ToArray() ?? [];
        return new DesignParameter(
            parameter.Attribute("Name")?.Value ?? "",
            parameter.Element(ns + "DataType")?.Value ?? "String",
            parameter.Element(ns + "Prompt")?.Value ?? "",
            defaults.Length > 0 ? defaults[0] : null,
            Flag(parameter, ns, "AllowBlank"),
            Flag(parameter, ns, "Nullable"),
            Flag(parameter, ns, "Hidden"),
            options,
            Flag(parameter, ns, "MultiValue"),
            optionsReference?.Element(ns + "DataSetName")?.Value,
            optionsReference?.Element(ns + "ValueField")?.Value,
            optionsReference?.Element(ns + "LabelField")?.Value,
            defaultReference?.Element(ns + "DataSetName")?.Value,
            defaultReference?.Element(ns + "ValueField")?.Value,
            defaults);
    }

    private static List<ParameterOption> ParseOptions(string? text, string type)
    {
        var options = new List<ParameterOption>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return options;
        }

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var bar = trimmed.IndexOf('|', StringComparison.Ordinal);
            var rawValue = bar < 0 ? trimmed : trimmed[..bar].Trim();
            var label = bar < 0 ? rawValue : Clip(trimmed[(bar + 1)..], 80);
            var value = Normalize(rawValue, type) ?? throw new ReportDesignException(ReportCodes.InvalidParameterValue);
            if (options.Any(option => string.Equals(option.Value, value, StringComparison.Ordinal)))
            {
                continue;
            }

            options.Add(new ParameterOption(value, label.Length == 0 ? value : label));
            if (options.Count > MaxOptions)
            {
                throw new ReportDesignException(ReportCodes.Limit);
            }
        }

        return options;
    }

    private static bool Flag(XElement parameter, XNamespace ns, string name) =>
        string.Equals(parameter.Element(ns + name)?.Value, "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsNil(XElement value) =>
        value.Attributes().Any(attribute => attribute.Name.LocalName == "nil" && string.Equals(attribute.Value, "true", StringComparison.OrdinalIgnoreCase));

    private static string Clip(string? text, int length)
    {
        var trimmed = text?.Trim() ?? "";
        return trimmed.Length > length ? trimmed[..length] : trimmed;
    }
}
