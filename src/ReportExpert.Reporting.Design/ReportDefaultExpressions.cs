using System.Globalization;
using System.Text.RegularExpressions;

namespace ReportExpert.Reporting.Design;

/// <summary>
/// Evaluates the few default-value expressions period reports use, so data sources can filter on the same
/// period the report engine will show: <c>=Year(Today())</c>, <c>=Month(Today())</c>, either with an
/// offset (<c>=Year(Today()) - 1</c>) or on a shifted month (<c>=Month(DateAdd("m", -1, Today()))</c>),
/// and <c>=Today()</c>. Any other expression is left to the report engine and does not filter the source.
/// </summary>
public static partial class ReportDefaultExpressions
{
    public static bool TryEvaluate(string expression, DateTime today, out string? value)
    {
        value = null;
        var text = expression.Trim();
        if (TodayOnly().IsMatch(text))
        {
            value = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return true;
        }

        var match = YearOrMonth().Match(text);
        if (!match.Success)
        {
            return false;
        }

        var date = today;
        if (match.Groups["shift"].Success)
        {
            date = date.AddMonths(int.Parse(match.Groups["shift"].Value.Replace(" ", "", StringComparison.Ordinal), CultureInfo.InvariantCulture));
        }

        var number = match.Groups["part"].Value.Equals("Year", StringComparison.OrdinalIgnoreCase) ? date.Year : date.Month;
        if (match.Groups["offset"].Success)
        {
            number += int.Parse(match.Groups["offset"].Value.Replace(" ", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
        }

        value = number.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    [GeneratedRegex(@"^=\s*(Today|Now)\(\)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TodayOnly();

    [GeneratedRegex(@"^=\s*(?<part>Year|Month)\(\s*(?:(?:Today|Now)\(\)|DateAdd\(\s*""m""\s*,\s*(?<shift>[+-]?\s*\d{1,3})\s*,\s*(?:Today|Now)\(\)\s*\))\s*\)\s*(?<offset>[+-]\s*\d{1,4})?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex YearOrMonth();
}
