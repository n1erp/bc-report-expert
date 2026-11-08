using System.Text.RegularExpressions;
using System.Xml.Linq;
using ReportExpert.Rdl.Core;

namespace ReportExpert.Reporting.Design;

/// <summary>
/// Works out the type of dataset fields the layout does not type.
/// </summary>
/// <remarks>
/// Business Central layouts declare their fields without <c>rd:TypeName</c>, so every field would be
/// previewed as text. The layout's own expressions say otherwise: <c>iif(Fields!Show.Value, …)</c> needs a
/// Boolean, <c>Sum(Fields!Amount.Value)</c> a number, <c>Code.BlankZero(Fields!X.Value)</c> whatever the code
/// function declares. Text there makes the report processor fail ("The input string … was not in a correct
/// format", "uses a numeric aggregate function on data that is not numeric"). Only evidence from the layout is
/// used — never the field name — so a field the layout treats as text stays text.
/// </remarks>
public static partial class ReportFieldTypeInference
{
    private const string Decimal = "System.Decimal";
    private const string Boolean = "System.Boolean";
    private const string DateTime = "System.DateTime";
    private const string Binary = "System.Byte[]";

    /// <summary>
    /// The dataset's fields for rendering: declared types are kept, untyped fields get the type their use implies
    /// (text when nothing implies otherwise).
    /// </summary>
    public static IReadOnlyList<ReportField> FieldsForRender(RdlDocument document, string dataSetName)
    {
        ArgumentNullException.ThrowIfNull(document);
        var dataset = document.Descendants("DataSet")
            .FirstOrDefault(candidate => string.Equals(candidate.Attribute("Name")?.Value, dataSetName, StringComparison.Ordinal));
        var fields = dataset?.Element(document.Ns + "Fields")?.Elements(document.Ns + "Field").ToList() ?? [];
        if (fields.Count == 0)
        {
            return [];
        }

        string? expressions = null;
        string? hidden = null;
        string? images = null;
        IReadOnlyDictionary<string, string>? codeTypes = null;
        var result = new List<ReportField>(fields.Count);
        var untyped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var name = field.Attribute("Name")?.Value ?? "";
            if (name.Length == 0)
            {
                continue;
            }

            var declared = field.Element(RdlDocument.Rd + "TypeName")?.Value;
            if (!string.IsNullOrWhiteSpace(declared))
            {
                result.Add(new ReportField(name, declared.Trim()));
                continue;
            }

            untyped.Add(name);
            expressions ??= Expressions(document);
            hidden ??= HiddenValues(document);
            images ??= DatabaseImageValues(document);
            codeTypes ??= CodeParameterTypes(document);
            result.Add(new ReportField(name, Infer(name, expressions, codeTypes, hidden, images) ?? "System.String"));
        }

        if (expressions is not null)
        {
            SpreadThroughAddition(result, untyped, expressions);
        }

        return result;
    }

    /// <summary>The type the expressions imply for a field, or <see langword="null"/> when they use it as text.</summary>
    internal static string? Infer(
        string fieldName,
        string expressions,
        IReadOnlyDictionary<string, string> codeTypes,
        string hiddenValues = "",
        string databaseImageValues = "")
    {
        var field = Field(fieldName);
        bool Has(string pattern) => Regex.IsMatch(expressions, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var operand = Operand(fieldName);

        // Binary: an image stored in the data, or bytes turned into Base64 for one.
        if (Has(@"\bConvert\.ToBase64String\s*\(\s*" + operand)
            || Regex.IsMatch(databaseImageValues, @"(^|\u0001)\s*=\s*" + operand + @"\s*(\u0001|$)", RegexOptions.IgnoreCase))
        {
            return Binary;
        }

        // Numbers: aggregates, conversions, number formatting, arithmetic, comparison with a number literal.
        if (Has(@"\b(Sum|Avg|StDev|StDevP|Var|VarP|RunningValue)\s*\(\s*(CDec\s*\(\s*|CDbl\s*\(\s*)?" + field)
            || Has(@"\b(CDec|CDbl|CInt|CLng|CSng|CShort|Val|FormatNumber|FormatCurrency|FormatPercent|Abs|Round|Int|Fix)\s*\(\s*" + operand)
            || Has(@"\bConvert\.To(Int16|Int32|Int64|Decimal|Double|Single)\s*\(\s*" + operand)
            || Has(@"\b(Integer|Int16|Int32|Int64|Long|Short|Decimal|Double|Single)\.Parse\s*\(\s*" + operand)
            || Has(operand + @"\s*[-*/\\^]\s*[\w(.]")
            || Has(@"[\w).]\s*[-*/\\^]\s*" + operand)
            || Has(operand + @"\s*(<>|<=|>=|=|<|>)\s*-?\(?\d")
            || Has(@"(?<![""\w])-?\d+(\.\d+)?\)?\s*(<>|<=|>=|=|<|>)\s*" + operand))
        {
            return Decimal;
        }

        // Booleans: a condition on its own, negated, combined with And/Or, or compared with True/False.
        if (Has(@"\b(CBool|Convert\.ToBoolean|Boolean\.Parse)\s*\(\s*" + operand)
            || Has(@"\biif\s*\(\s*(Not\s+)?" + operand + @"\s*,")
            || Has(@"\bNot\s+" + operand)
            || Has(operand + @"\s*(=|<>)\s*(True|False)\b")
            || Has(@"\b(True|False)\s*(=|<>)\s*" + operand)
            || Has(@"\b(And|Or|AndAlso|OrElse|Xor)\s+(Not\s+)?" + operand)
            || Has(operand + @"\s+(And|Or|AndAlso|OrElse|Xor)\b")
            || Regex.IsMatch(hiddenValues, @"(^|\u0001)\s*=\s*(Not\s+)?" + operand + @"\s*(\u0001|$)", RegexOptions.IgnoreCase))
        {
            return Boolean;
        }

        // Dates: date functions.
        if (Has(@"\b(FormatDateTime|Year|Month|Day|Weekday|DatePart|CDate)\s*\(\s*" + field)
            || Has(@"\b(DateAdd|DateDiff)\s*\([^()\u0001]*" + field))
        {
            return DateTime;
        }

        // Custom code: the type the function declares for the parameter the field is passed to.
        foreach (Match call in Regex.Matches(expressions, @"\bCode\.(\w+)\s*\(\s*" + field, RegexOptions.IgnoreCase))
        {
            if (codeTypes.TryGetValue(call.Groups[1].Value, out var type))
            {
                return type;
            }
        }

        return null;
    }

    private static string Field(string fieldName) => @"Fields!" + Regex.Escape(fieldName) + @"\.Value\b";

    /// <summary>The field itself, or the field inside a value-preserving aggregate such as Last(Fields!X.Value, "DataSet").</summary>
    private static string Operand(string fieldName)
    {
        var field = Field(fieldName);
        return @"(" + field + @"|\b(First|Last|Max|Min|Previous|Sum|Avg)\s*\(\s*" + field + @"[^()\u0001]*\))";
    }

    /// <summary>
    /// VB's <c>+</c> turns text into a number when the other side is a number ("NNCVATAmt 1" + 2.5 fails), so a
    /// field added to a numeric field or a number literal is numeric too. Repeats until nothing changes, which
    /// carries the type along chains such as <c>A + B + C</c>.
    /// </summary>
    private static void SpreadThroughAddition(List<ReportField> fields, HashSet<string> untyped, string expressions)
    {
        static bool IsNumeric(string type) => type is Decimal or "System.Double" or "System.Single" or "System.Int32" or "System.Int64" or "System.Int16";

        bool changed;
        do
        {
            changed = false;
            var numeric = fields.Where(f => IsNumeric(f.DataType)).Select(f => Operand(f.Name)).ToList();
            if (numeric.Count == 0)
            {
                return;
            }

            var other = "(" + string.Join("|", numeric) + @"|-?\d+(\.\d+)?\b)";
            for (var i = 0; i < fields.Count; i++)
            {
                var candidate = fields[i];
                if (!untyped.Contains(candidate.Name) || candidate.DataType != "System.String")
                {
                    continue;
                }

                var operand = Operand(candidate.Name);
                if (Regex.IsMatch(expressions, operand + @"\s*\+\s*" + other, RegexOptions.IgnoreCase)
                    || Regex.IsMatch(expressions, other + @"\s*\+\s*" + operand, RegexOptions.IgnoreCase))
                {
                    fields[i] = candidate with { DataType = Decimal };
                    changed = true;
                }
            }
        }
        while (changed);
    }

    /// <summary>
    /// Separates expressions. The patterns' <c>\s</c> and <c>[^()…]</c> never cross it, so the end of one
    /// expression ("…GetData(84,1)") cannot pair with the start of the next ("= Fields!X.Value").
    /// </summary>
    internal const char Separator = '\u0001';

    /// <summary>Every expression in the layout (values starting with <c>=</c>), separated by <see cref="Separator"/>.</summary>
    internal static string Expressions(RdlDocument document) =>
        string.Join(Separator, document.Root.DescendantNodes()
            .OfType<XText>()
            .Select(text => text.Value.Trim())
            .Where(value => value.StartsWith('=')));

    /// <summary>
    /// The first parameter type of each function in the report's custom code, for the types REX can supply:
    /// <c>Public Function BlankZero(ByVal Value As Decimal)</c> → BlankZero = Decimal.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> CodeParameterTypes(RdlDocument document)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var code = document.Root.Element(document.Ns + "Code")?.Value;
        if (string.IsNullOrWhiteSpace(code))
        {
            return map;
        }

        foreach (Match function in CodeFunction().Matches(code))
        {
            var type = function.Groups["type"].Value.ToLowerInvariant() switch
            {
                "decimal" or "double" or "single" or "integer" or "long" or "short" => Decimal,
                "boolean" => Boolean,
                "date" or "datetime" => DateTime,
                _ => null,
            };
            if (type is not null)
            {
                map.TryAdd(function.Groups["name"].Value, type);
            }
        }

        return map;
    }

    /// <summary>
    /// The <c>Hidden</c> values, separated by <see cref="Separator"/>. A bare <c>=Fields!X.Value</c> there is a Boolean; elsewhere it is
    /// only displayed, and text is always safe to display.
    /// </summary>
    internal static string DatabaseImageValues(RdlDocument document) =>
        string.Join(Separator, document.Descendants("Image")
            .Where(image => string.Equals(image.Element(document.Ns + "Source")?.Value.Trim(), "Database", StringComparison.OrdinalIgnoreCase))
            .Select(image => image.Element(document.Ns + "Value")?.Value.Trim() ?? ""));

    internal static string HiddenValues(RdlDocument document) =>
        string.Join(Separator, document.Descendants("Hidden").Select(hidden => hidden.Value.Trim()));

    [GeneratedRegex(@"Function\s+(?<name>\w+)\s*\(\s*(ByVal\s+|ByRef\s+)?\w+\s+As\s+(?<type>\w+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CodeFunction();
}
