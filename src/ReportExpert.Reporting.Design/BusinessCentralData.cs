using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace ReportExpert.Reporting.Design;

/// <summary>
/// Serves the datasets a Business Central layout declares (usually <c>DataSet_Result</c>). The layout's own
/// fields are the schema. Rows come from the data file attached to the report: the XML Business Central writes
/// from a request page (<em>Send to… › XML document</em>, the <c>DataItems</c> format), a dataset XML, JSON or CSV.
/// Without a file the provider generates sample rows from the field types, so a layout can always be previewed.
/// </summary>
public sealed class BusinessCentralDataProvider(BusinessCentralDataStore store, Microsoft.Extensions.Options.IOptions<ReportingOptions> options)
    : IReportDataProvider, IReportDataSetReader, IReportDataSourceInfo
{
    public string Key => ReportDataCommand.BusinessCentralKey;

    public string DataSetName => "DataSet_Result";

    public string ResourceKey => "Source.BusinessCentral";

    public IReadOnlyList<ReportField> Fields => [];

    public string Group => "Business Central";

    public string Label => "Layout dataset (BC XML, JSON, CSV or sample data)";

    public bool IsView => false;

    public Task<DataTable> ReadAsync(IReadOnlyDictionary<string, string?> parameters, int maxRows, CancellationToken cancellationToken) =>
        Task.FromResult(new DataTable(DataSetName));

    public async Task<DataTable> ReadAsync(ReportDataSetRequest request, IReadOnlyDictionary<string, string?> parameters, int maxRows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var file = await store.ResolveAsync(request.ReportId, cancellationToken);
        if (file is not null && File.Exists(file))
        {
            var source = BusinessCentralDataReader.Read(file, request.DataSetName);
            return BusinessCentralDataReader.Shape(source, request.DataSetName, request.Fields, maxRows);
        }

        // Sample rows follow Settings › Preview › Sample rows (read on every render, so a change applies on refresh).
        var sample = Math.Clamp(options.Value.SampleRows, 1, ReportingOptions.MaxSampleRows);
        return BusinessCentralDataReader.Sample(request.DataSetName, request.Fields, Math.Clamp(maxRows, 1, sample));
    }
}

/// <summary>
/// Remembers which data file feeds a report. An explicit attachment wins; otherwise a file next to the linked
/// RDLC with the same name and a <c>.xml</c>, <c>.json</c> or <c>.csv</c> extension is used, which is the
/// convention REX has always followed.
/// </summary>
public sealed class BusinessCentralDataStore(ReportStore reports)
{
    private static readonly string[] SiblingExtensions = [".xml", ".json", ".csv"];

    public Task AttachAsync(Guid reportId, string? dataPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        reports.SetDataFile(reportId, string.IsNullOrWhiteSpace(dataPath) ? null : Path.GetFullPath(dataPath));
        return Task.CompletedTask;
    }

    /// <summary>The data file for a report, or null when the report previews with sample rows.</summary>
    public async Task<string?> ResolveAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var attached = reports.DataFile(reportId);
        if (!string.IsNullOrEmpty(attached) && File.Exists(attached))
        {
            return attached;
        }

        var source = await reports.SourcePathAsync(reportId, cancellationToken);
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var folder = Path.GetDirectoryName(source) ?? "";
        var stem = Path.GetFileNameWithoutExtension(source);
        foreach (var extension in SiblingExtensions)
        {
            var candidate = Path.Combine(folder, stem + extension);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

/// <summary>Parses Business Central and generic data files into tables shaped like a layout dataset.</summary>
public static class BusinessCentralDataReader
{
    /// <summary>Reads a data file. Business Central <c>DataItems</c> XML is flattened the way the BC runtime does.</summary>
    public static DataTable Read(string path, string dataSetName)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".json" => ReadJson(File.ReadAllText(path), dataSetName),
            ".csv" => ReadCsv(File.ReadAllText(path), dataSetName),
            _ => ReadXml(XDocument.Load(path), dataSetName),
        };
    }

    public static DataTable ReadXml(XDocument document, string dataSetName)
    {
        ArgumentNullException.ThrowIfNull(document);
        var dataItems = document.Root?.Name.LocalName == "DataItems"
            ? document.Root
            : document.Descendants().FirstOrDefault(element => element.Name.LocalName == "DataItems");
        if (dataItems is not null)
        {
            var rows = new List<List<(string Name, string Value)>>();
            Flatten(dataItems, [], rows);
            return Build(dataSetName, rows);
        }

        // Dataset XML (DataTable.WriteXml) or any <Root><Row><Column/></Row></Root> file.
        var root = document.Root ?? throw new InvalidDataException("The XML file is empty.");
        var records = root.Elements().Where(element => element.Name.LocalName == dataSetName).ToArray();
        if (records.Length == 0)
        {
            records = root.Elements().Where(element => element.HasElements).ToArray();
        }

        var flat = records
            .Select(record => record.Elements().Select(column => (column.Name.LocalName, column.Value)).ToList())
            .ToList();
        return Build(dataSetName, flat);
    }

    public static DataTable ReadJson(string json, string dataSetName)
    {
        using var document = JsonDocument.Parse(json);
        var array = document.RootElement;
        if (array.ValueKind == JsonValueKind.Object)
        {
            array = array.TryGetProperty(dataSetName, out var named) ? named
                : array.EnumerateObject().Select(property => property.Value).FirstOrDefault(value => value.ValueKind == JsonValueKind.Array);
        }

        var rows = new List<List<(string Name, string Value)>>();
        if (array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object))
            {
                rows.Add(item.EnumerateObject()
                    .Select(property => (property.Name, property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString() ?? "",
                        JsonValueKind.Null => "",
                        _ => property.Value.GetRawText(),
                    }))
                    .ToList());
            }
        }

        return Build(dataSetName, rows);
    }

    public static DataTable ReadCsv(string csv, string dataSetName)
    {
        var lines = SplitCsv(csv).ToList();
        if (lines.Count == 0)
        {
            return new DataTable(dataSetName);
        }

        var header = lines[0];
        var rows = lines.Skip(1)
            .Where(line => line.Count > 0 && !(line.Count == 1 && line[0].Length == 0))
            .Select(line => header.Select((name, index) => (name, index < line.Count ? line[index] : "")).ToList())
            .ToList();
        return Build(dataSetName, rows);
    }

    /// <summary>
    /// Shapes raw rows to the dataset the layout declares: every declared field becomes a column of its
    /// <c>rd:TypeName</c>; columns are matched by name, ignoring case and the characters BC replaces with
    /// underscores. Undeclared columns are dropped, missing ones stay empty.
    /// </summary>
    public static DataTable Shape(DataTable source, string dataSetName, IReadOnlyList<ReportField> fields, int maxRows)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (fields.Count == 0)
        {
            source.TableName = dataSetName;
            return source;
        }

        var table = new DataTable(dataSetName);
        var map = new List<(DataColumn Target, int Source)>();
        foreach (var field in fields)
        {
            var column = table.Columns.Add(field.Name, ClrType(field.DataType));
            var index = FindColumn(source, field.Name);
            map.Add((column, index));
        }

        foreach (DataRow row in source.Rows)
        {
            if (table.Rows.Count >= maxRows)
            {
                break;
            }

            var target = table.NewRow();
            foreach (var (column, index) in map)
            {
                if (index >= 0)
                {
                    target[column] = Convert(row[index]?.ToString(), column.DataType);
                }
            }

            table.Rows.Add(target);
        }

        source.Dispose();
        return table;
    }

    /// <summary>Generates readable sample rows from the declared field types.</summary>
    public static DataTable Sample(string dataSetName, IReadOnlyList<ReportField> fields, int rows)
    {
        var table = new DataTable(dataSetName);
        foreach (var field in fields)
        {
            table.Columns.Add(field.Name, ClrType(field.DataType));
        }

        for (var i = 0; i < rows; i++)
        {
            var row = table.NewRow();
            foreach (DataColumn column in table.Columns)
            {
                row[column] = SampleValue(column, i);
            }

            table.Rows.Add(row);
        }

        return table;
    }

    public static Type ClrType(string? typeName) => (typeName ?? "").Trim() switch
    {
        "System.Int32" or "System.Int16" or "Integer" => typeof(int),
        "System.Int64" or "BigInteger" => typeof(long),
        "System.Decimal" or "Decimal" => typeof(decimal),
        "System.Double" or "System.Single" or "Float" => typeof(double),
        "System.DateTime" or "DateTime" or "Date" => typeof(DateTime),
        "System.Boolean" or "Boolean" => typeof(bool),
        "System.Byte[]" => typeof(byte[]),
        "System.Guid" => typeof(Guid),
        _ => typeof(string),
    };

    /// <summary>
    /// The picture sample data uses for image fields (company logos, item pictures): a light grey 240×180
    /// card with a picture icon and "Sample image", so the layout shows where an image goes without
    /// pretending to be real content.
    /// </summary>
    private static readonly byte[] SampleImage = System.Convert.FromBase64String(
        "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");

    private static object SampleValue(DataColumn column, int index)
    {
        var name = column.ColumnName;
        if (column.DataType == typeof(int) || column.DataType == typeof(long))
        {
            return System.Convert.ChangeType(index + 1, column.DataType, CultureInfo.InvariantCulture);
        }

        if (column.DataType == typeof(decimal))
        {
            return Math.Round(100m + index * 37.25m, 2);
        }

        if (column.DataType == typeof(double))
        {
            return 100d + index * 37.25d;
        }

        if (column.DataType == typeof(DateTime))
        {
            return new DateTime(2026, 1, 1).AddDays(index * 3);
        }

        if (column.DataType == typeof(bool))
        {
            return index % 2 == 0;
        }

        if (column.DataType == typeof(byte[]))
        {
            // A neutral placeholder PNG: pictures and Convert.ToBase64String(Fields!Picture.Value) both work with it.
            return (byte[])SampleImage.Clone();
        }

        if (column.DataType == typeof(Guid))
        {
            return Guid.Empty;
        }

        if (name.EndsWith("Caption", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Lbl", StringComparison.OrdinalIgnoreCase))
        {
            return Humanize(name);
        }

        if (name.EndsWith("Format", StringComparison.OrdinalIgnoreCase))
        {
            return "#,##0.00";
        }

        return Humanize(name) + " " + (index + 1).ToString(CultureInfo.InvariantCulture);
    }

    private static string Humanize(string name)
    {
        var text = name;
        foreach (var suffix in new[] { "Caption", "Lbl" })
        {
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && text.Length > suffix.Length)
            {
                text = text[..^suffix.Length];
            }
        }

        var builder = new StringBuilder();
        foreach (var ch in text.Replace('_', ' '))
        {
            if (char.IsUpper(ch) && builder.Length > 0 && builder[^1] != ' ' && !char.IsUpper(builder[^1]))
            {
                builder.Append(' ');
            }

            builder.Append(ch);
        }

        return builder.ToString().Trim();
    }

    private static int FindColumn(DataTable source, string name)
    {
        var wanted = Normalize(name);
        for (var i = 0; i < source.Columns.Count; i++)
        {
            if (Normalize(source.Columns[i].ColumnName) == wanted)
            {
                return i;
            }
        }

        return -1;
    }

    private static string Normalize(string name) =>
        new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static object Convert(string? value, Type type)
    {
        if (string.IsNullOrEmpty(value))
        {
            return type == typeof(string) ? "" : DBNull.Value;
        }

        try
        {
            if (type == typeof(string))
            {
                return value;
            }

            if (type == typeof(decimal))
            {
                return decimal.Parse(value, NumberStyles.Any, CultureInfo.InvariantCulture);
            }

            if (type == typeof(double))
            {
                return double.Parse(value, NumberStyles.Any, CultureInfo.InvariantCulture);
            }

            if (type == typeof(int))
            {
                return int.Parse(value, NumberStyles.Any, CultureInfo.InvariantCulture);
            }

            if (type == typeof(long))
            {
                return long.Parse(value, NumberStyles.Any, CultureInfo.InvariantCulture);
            }

            if (type == typeof(DateTime))
            {
                return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces);
            }

            if (type == typeof(bool))
            {
                return value is "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
            }

            if (type == typeof(byte[]))
            {
                return System.Convert.FromBase64String(value);
            }

            if (type == typeof(Guid))
            {
                return Guid.Parse(value);
            }
        }
        catch (FormatException)
        {
            return DBNull.Value;
        }
        catch (OverflowException)
        {
            return DBNull.Value;
        }

        return value;
    }

    private static void Flatten(XElement dataItems, List<(string Name, string Value)> inherited, List<List<(string Name, string Value)>> result)
    {
        foreach (var item in dataItems.Elements().Where(element => element.Name.LocalName == "DataItem"))
        {
            var own = (item.Elements().FirstOrDefault(element => element.Name.LocalName == "Columns")?.Elements() ?? [])
                .Where(column => column.Name.LocalName == "Column")
                .Select(column => (column.Attribute("name")?.Value ?? "?", column.Value))
                .ToList();
            var combined = new List<(string Name, string Value)>(inherited.Count + own.Count);
            combined.AddRange(inherited);
            combined.AddRange(own);

            var nested = item.Elements().FirstOrDefault(element => element.Name.LocalName == "DataItems");
            if (nested is not null && nested.Elements().Any(element => element.Name.LocalName == "DataItem"))
            {
                Flatten(nested, combined, result);
            }
            else
            {
                result.Add(combined);
            }
        }
    }

    private static DataTable Build(string dataSetName, List<List<(string Name, string Value)>> rows)
    {
        var table = new DataTable(dataSetName);
        foreach (var row in rows)
        {
            foreach (var (name, _) in row)
            {
                if (!table.Columns.Contains(name))
                {
                    table.Columns.Add(name, typeof(string));
                }
            }
        }

        foreach (var row in rows)
        {
            var target = table.NewRow();
            foreach (var (name, value) in row)
            {
                target[name] = value;
            }

            table.Rows.Add(target);
        }

        return table;
    }

    private static IEnumerable<List<string>> SplitCsv(string csv)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var ch = csv[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(ch);
                }
            }
            else if (ch == '"')
            {
                quoted = true;
            }
            else if (ch is ',' or ';')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\n')
            {
                row.Add(field.ToString().TrimEnd('\r'));
                field.Clear();
                yield return row;
                row = [];
            }
            else
            {
                field.Append(ch);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString().TrimEnd('\r'));
            yield return row;
        }
    }
}
