using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace ReportExpert.Common;

/// <summary>
/// What the error dialog shows for an exception: a short message for the developer, the full technical
/// details (versions, operation, exception chain with stack traces) for copying, and a prefilled GitHub bug
/// report. Personal paths are replaced by <c>%USERPROFILE%</c> and secrets that look like keys are masked.
/// </summary>
public sealed class ErrorReport
{
    private ErrorReport(string title, string message, string details, string operation, Exception exception, string? summary = null)
    {
        Summary = summary;
        Title = title;
        Message = message;
        Details = details;
        Operation = operation;
        Exception = exception;
    }

    /// <summary>The dialog title, for example "Unexpected error".</summary>
    public string Title { get; }

    /// <summary>One or two sentences: what failed and the exception message.</summary>
    public string Message { get; }

    /// <summary>The full report to copy or attach to an issue.</summary>
    public string Details { get; }

    /// <summary>What REX was doing (for example "Designer: Add group"), or "" when unknown.</summary>
    public string Operation { get; }

    public Exception Exception { get; }

    /// <summary>The caller's own explanation (for example the report engine's words), when it gave one.</summary>
    public string? Summary { get; }

    /// <summary>Builds the report for an exception.</summary>
    /// <param name="exception">The failure.</param>
    /// <param name="operation">What the user was doing, shown first in the details.</param>
    /// <param name="title">Dialog title; defaults to "Unexpected error".</param>
    /// <param name="summary">
    /// What went wrong in plain words, when the caller knows better than the exception message (for example
    /// "The report could not be rendered. The Hidden expression for the tablix 'table2' contains an error…").
    /// </param>
    public static ErrorReport Create(Exception exception, string? operation = null, string? title = null, string? summary = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var root = Unwrap(exception);
        var message = Redact(string.IsNullOrWhiteSpace(root.Message) ? root.GetType().Name : root.Message.Trim());
        summary = string.IsNullOrWhiteSpace(summary) ? null : Redact(summary.Trim());
        var friendly = summary is not null
            ? (string.IsNullOrWhiteSpace(operation) ? summary : $"{operation.Trim()} failed. {summary}")
            : string.IsNullOrWhiteSpace(operation)
                ? $"{root.GetType().Name}: {message}"
                : $"{operation.Trim()} failed. {root.GetType().Name}: {message}";

        var details = new StringBuilder();
        details.AppendLine(CultureInfo.InvariantCulture, $"{AppConstants.ApplicationName} {VersionText()}");
        details.AppendLine(CultureInfo.InvariantCulture, $"Time (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        details.AppendLine(CultureInfo.InvariantCulture, $"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        details.AppendLine(CultureInfo.InvariantCulture, $".NET: {RuntimeInformation.FrameworkDescription}");
        details.AppendLine(CultureInfo.InvariantCulture, $"Culture: {CultureInfo.CurrentCulture.Name} / UI {CultureInfo.CurrentUICulture.Name}");
        if (!string.IsNullOrWhiteSpace(operation))
        {
            details.AppendLine(CultureInfo.InvariantCulture, $"Operation: {operation.Trim()}");
        }

        if (summary is not null)
        {
            details.AppendLine(CultureInfo.InvariantCulture, $"Summary: {summary}");
        }

        details.AppendLine();
        var depth = 0;
        for (var current = exception; current is not null && depth < 8; current = current.InnerException, depth++)
        {
            details.AppendLine(CultureInfo.InvariantCulture, $"{(depth == 0 ? "Exception" : "Inner exception " + depth)}: {current.GetType().FullName}");
            details.AppendLine(CultureInfo.InvariantCulture, $"Message: {current.Message}");
            if (DetailOf(current) is { Length: > 0 } detail)
            {
                details.AppendLine(CultureInfo.InvariantCulture, $"Detail: {detail}");
            }

            if (current.HResult != 0)
            {
                details.AppendLine(CultureInfo.InvariantCulture, $"HResult: 0x{current.HResult:X8}");
            }

            if (!string.IsNullOrWhiteSpace(current.StackTrace))
            {
                details.AppendLine("Stack trace:");
                details.AppendLine(current.StackTrace);
            }

            if (current is AggregateException aggregate && aggregate.InnerExceptions.Count > 1)
            {
                details.AppendLine(CultureInfo.InvariantCulture, $"(+{aggregate.InnerExceptions.Count - 1} more inner exceptions)");
            }

            details.AppendLine();
        }

        return new ErrorReport(title ?? "Unexpected error", friendly, Redact(details.ToString().TrimEnd()), operation?.Trim() ?? "", exception, summary);
    }

    /// <summary>A prefilled GitHub bug report for this error (the bug report form's fields).</summary>
    public string BugReportUrl()
    {
        var root = Unwrap(Exception);
        var title = "[Bug] " + (Operation.Length > 0 ? Operation + ": " : "")
            + (Summary is not null ? FirstLine(Summary) : root.GetType().Name + ": " + FirstLine(Redact(root.Message)));
        return ProjectLinks.NewIssue(ProjectLinks.BugTemplate, title, new Dictionary<string, string>
        {
            ["version"] = VersionText(),
            ["os"] = RuntimeInformation.OSDescription,
            ["summary"] = Message,
            ["logs"] = Details,
        });
    }

    /// <summary>The REX version shown in reports, for example "rex-2.0-baboon".</summary>
    public static string VersionText()
    {
        var version = AppConstants.ApplicationVersionDisplay;
        return version.Length > 0 ? version : "unknown";
    }

    /// <summary>
    /// Removes personal data from a report: the user profile path becomes <c>%USERPROFILE%</c>, the user
    /// name becomes <c>%USERNAME%</c>, and values that look like API keys are masked.
    /// </summary>
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 3)
        {
            text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        var user = Environment.UserName;
        if (user.Length >= 3)
        {
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"(?<=[\\/])" + System.Text.RegularExpressions.Regex.Escape(user) + @"(?=[\\/])",
                "%USERNAME%",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // sk-…, ghp_…, Bearer tokens and api-key=… values.
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(sk|pk|ghp|gho|xox[abp])[-_][A-Za-z0-9_\-]{12,}", "$1-***");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?i)(bearer\s+)[A-Za-z0-9\.\-_]{12,}", "$1***");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?i)((api[-_]?key|password|secret|token)\s*[=:]\s*)[^\s;""']+", "$1***");
        return text;
    }

    /// <summary>The exception that says what really failed (target invocation and single aggregates unwrapped).</summary>
    private static Exception Unwrap(Exception exception)
    {
        var current = exception;
        while (true)
        {
            switch (current)
            {
                case System.Reflection.TargetInvocationException { InnerException: { } inner }:
                    current = inner;
                    continue;
                case AggregateException aggregate when aggregate.InnerExceptions.Count == 1:
                    current = aggregate.InnerExceptions[0];
                    continue;
                default:
                    return current;
            }
        }
    }

    /// <summary>A <c>Detail</c> property some REX exceptions carry (for example the report engine's own words).</summary>
    private static string? DetailOf(Exception exception) =>
        exception.GetType().GetProperty("Detail")?.GetValue(exception) as string;

    private static string FirstLine(string text)
    {
        var line = text.Split('\n')[0].Trim();
        return line.Length > 140 ? line[..140] + "…" : line;
    }
}

/// <summary>
/// Lets libraries without a UI (the preview module, for example) show the error dialog: the application sets
/// <see cref="Handler"/> at start-up; without one, <see cref="Show"/> does nothing.
/// </summary>
public static class ErrorReporting
{
    /// <summary>Shows the error dialog: exception, operation, title, summary.</summary>
    public static Action<Exception, string?, string?, string?>? Handler { get; set; }

    /// <summary>Shows the error dialog for <paramref name="exception"/>. Never throws.</summary>
    public static void Show(Exception exception, string? operation = null, string? title = null, string? summary = null)
    {
        try
        {
            Handler?.Invoke(exception, operation, title, summary);
        }
        catch (Exception)
        {
            // The dialog must never cause a second failure.
        }
    }
}
