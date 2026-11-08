using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ReportExpert.Rdl.Core;

namespace ReportExpert.Reporting.Design;

/// <summary>
/// Turns a report engine failure into a short explanation for the designer: the messages of the
/// ReportViewer exceptions in the chain, without the generic wrappers, stack traces or paths.
/// Messages from other code (database drivers, the file system) are left out because they can carry
/// connection or server details; the full exception stays in the application log.
/// </summary>
public static class ReportFailure
{
    private const int MaxLength = 1200;

    private static readonly string[] Wrappers =
    [
        "An error occurred during local report processing.",
        "An error occurred during rendering of the report.",
        "An error has occurred during report processing.",
    ];

    private const string SchemaPreamble = "The definition of this report is not valid or supported by this version of Reporting Services.";

    public static string? Describe(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PlatformNotSupportedException || current is DllNotFoundException)
            {
                parts.Add("PDF, Excel and Word output needs Windows: the report engine draws through GDI+, which this server does not have.");
                break;
            }

            var origin = current.GetType().Namespace ?? "";
            if (!origin.StartsWith("Microsoft.Reporting", StringComparison.Ordinal))
            {
                continue;
            }

            var message = Clean(current.Message);
            if (message.Length == 0 || Wrappers.Contains(message, StringComparer.Ordinal) || message.StartsWith("The definition of the report '", StringComparison.Ordinal))
            {
                continue;
            }

            if (!parts.Contains(message, StringComparer.Ordinal))
            {
                parts.Add(message);
            }
        }

        return parts.Count == 0 ? null : Limit(string.Join(" ", parts));
    }

    /// <summary>
    /// When the engine names an expression ("The Hidden expression for the tablix ‘table2’ contains an
    /// error: …"), quotes that expression from the definition and describes the fields it reads in the data the
    /// preview used (type, and how many values are empty), with a hint for the usual causes. Null when the
    /// message names no expression or it cannot be found.
    /// </summary>
    public static string? ExplainExpression(RdlDocument document, string? detail, IReadOnlyList<DataTable> tables)
    {
        if (string.IsNullOrEmpty(detail))
        {
            return null;
        }

        var match = Regex.Match(detail, @"The (?<property>[A-Za-z ]+?) expression for the (?<kind>[A-Za-z ]+?) ['‘’""](?<name>[^'‘’""]+)['‘’""]", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups["name"].Value;
        var property = match.Groups["property"].Value.Replace(" ", "", StringComparison.Ordinal);
        var item = document.Root.Descendants().FirstOrDefault(element => element.Attribute("Name")?.Value == name);
        var expression = item is null ? null : ExpressionOf(item, property);
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{match.Groups["property"].Value} expression of {name}: {Shorten(expression.Trim(), 400)}");

        var fields = Regex.Matches(expression, @"Fields!(?<field>\w+)\.Value", RegexOptions.IgnoreCase)
            .Select(found => found.Groups["field"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var described = new List<string>();
        var empties = new List<string>();
        foreach (var field in fields.Take(12))
        {
            var column = tables.Select(table => table.Columns[field]).FirstOrDefault(found => found is not null);
            if (column is null)
            {
                described.Add($"{field} (not in the data)");
                continue;
            }

            var rows = column.Table!.Rows.Count;
            var empty = column.Table.Rows.Cast<DataRow>().Count(row => row.IsNull(column) || row[column] is string { Length: 0 });
            described.Add(empty > 0 ? $"{field} ({Kind(column.DataType)}, {empty} of {rows} rows empty)" : $"{field} ({Kind(column.DataType)})");
            if (empty > 0)
            {
                empties.Add(field);
            }
        }

        if (described.Count > 0)
        {
            text.Append(". Fields: ").Append(string.Join(", ", described));
        }

        if (detail.Contains("not in a correct format", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("Conversion from string", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("Input string", StringComparison.OrdinalIgnoreCase))
        {
            var example = empties.FirstOrDefault() ?? fields.FirstOrDefault() ?? "Field";
            text.Append(". The expression turns text into a number or True/False, and some of that text is empty")
                .Append(" (data files often leave a value empty when it does not apply to the row).")
                .Append(CultureInfo.InvariantCulture, $" Guard it, for example =IIF(Fields!{example}.Value = \"\", 0, Fields!{example}.Value), or give the field a type in the dataset.");
        }

        return Limit(text.ToString());
    }

    /// <summary>The expression of a property of a report item, as the engine names it (Hidden, Value, Color…).</summary>
    private static string? ExpressionOf(XElement item, string property)
    {
        var ns = item.Name.Namespace;
        if (property.Equals("Hidden", StringComparison.OrdinalIgnoreCase))
        {
            return item.Element(ns + "Visibility")?.Element(ns + "Hidden")?.Value;
        }

        // Search the item's own properties, not those of the items it contains.
        return item.DescendantsAndSelf()
            .Where(element => !element.Ancestors().TakeWhile(ancestor => ancestor != item).Any(ancestor => ancestor.Name.LocalName is "ReportItems" or "CellContents"))
            .FirstOrDefault(element => element.Name.LocalName.Equals(property, StringComparison.OrdinalIgnoreCase) && !element.HasElements)
            ?.Value;
    }

    private static string Kind(Type type) =>
        type == typeof(string) ? "text"
        : type == typeof(bool) ? "True/False"
        : type == typeof(DateTime) ? "date"
        : type == typeof(byte[]) ? "binary"
        : "number";

    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..length] + "…";

    public static string Limit(string text) => text.Length <= MaxLength ? text : text[..MaxLength] + "…";

    private static string Clean(string message)
    {
        var text = message.Trim();
        var details = text.IndexOf("Details:", StringComparison.Ordinal);
        if (text.StartsWith(SchemaPreamble, StringComparison.Ordinal) && details > 0)
        {
            text = text[(details + "Details:".Length)..].Trim();
        }

        // Schema messages repeat the RDL namespace after every element name.
        text = text.Replace(" in namespace 'http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition'", "", StringComparison.Ordinal);
        return text.ReplaceLineEndings(" ");
    }
}
