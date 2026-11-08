namespace ReportExpert.Reporting.Design;

/// <summary>
/// A report operation was rejected. <see cref="Exception.Message"/> is a <see cref="ReportCodes"/> value.
/// </summary>
public sealed class ReportDesignException : Exception
{
    public ReportDesignException(string code, Exception? inner = null)
        : base(code, inner)
    {
    }

    public ReportDesignException(string code, string? detail, Exception? inner = null)
        : base(code, inner)
    {
        Detail = detail;
    }

    /// <summary>
    /// Why the operation failed, in the words of the report engine (for example the item and expression that
    /// do not compile). Shown to the designer under the localized message; never contains a stack trace,
    /// a file path or database text.
    /// </summary>
    public string? Detail { get; }
}
