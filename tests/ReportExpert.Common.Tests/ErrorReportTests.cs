namespace ReportExpert.Common.Tests;

public sealed class ErrorReportTests
{
    [Fact]
    public void The_report_names_the_operation_and_the_root_cause()
    {
        var exception = Throw(() => throw new InvalidOperationException("outer", new FormatException("bad number")));

        var report = ErrorReport.Create(exception, "Designer: Add group");

        Assert.StartsWith("Designer: Add group failed. InvalidOperationException: outer", report.Message, StringComparison.Ordinal);
        Assert.Contains("Operation: Designer: Add group", report.Details, StringComparison.Ordinal);
        Assert.Contains("Inner exception 1: System.FormatException", report.Details, StringComparison.Ordinal);
        Assert.Contains("Stack trace:", report.Details, StringComparison.Ordinal);
        Assert.Contains(AppConstants.ApplicationName, report.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrapper_exceptions_are_unwrapped_for_the_message()
    {
        var report = ErrorReport.Create(new AggregateException(new TimeoutException("too slow")));

        Assert.Contains("TimeoutException: too slow", report.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Personal_paths_and_secrets_are_redacted()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var text = ErrorReport.Redact($"Could not open {profile}{Path.DirectorySeparatorChar}report.rdlc with api_key=abc123secret and sk-ABCDEFGHIJKLMNOPQRS");

        if (profile.Length > 3)
        {
            Assert.DoesNotContain(profile, text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("%USERPROFILE%", text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("abc123secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ABCDEFGHIJKLMNOPQRS", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bug_report_link_opens_the_bug_form_of_the_repository_prefilled()
    {
        var report = ErrorReport.Create(Throw(() => throw new IOException("disk full")), "Save");

        var url = report.BugReportUrl();

        Assert.StartsWith("https://github.com/n1erp/bc-report-expert/issues/new?template=bug_report.yml", url, StringComparison.Ordinal);
        Assert.Contains("&title=", url, StringComparison.Ordinal);
        Assert.Contains("&logs=", url, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("disk full"), url, StringComparison.Ordinal);
        Assert.True(url.Length <= ProjectLinks.MaxUrlLength);
    }

    [Fact]
    public void Long_details_are_truncated_to_keep_the_url_valid()
    {
        var url = ProjectLinks.NewIssue(ProjectLinks.BugTemplate, "Big", new Dictionary<string, string> { ["logs"] = new string('x', 50_000) });

        Assert.True(url.Length <= ProjectLinks.MaxUrlLength, $"url is {url.Length} characters");
        Assert.Contains(Uri.EscapeDataString("(truncated"), url, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_links_point_at_the_repository()
    {
        Assert.Equal("https://github.com/n1erp/bc-report-expert", ProjectLinks.RepositoryUrl);
        Assert.Equal("https://github.com/n1erp/bc-report-expert/issues/", ProjectLinks.IssuesUrl);
    }

    private static Exception Throw(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new InvalidOperationException("did not throw");
    }

    [Fact]
    public void A_summary_replaces_a_coded_exception_message()
    {
        var failure = new InvalidOperationException("Report.RenderFailed", new FormatException("The input string '' was not in a correct format."));
        var report = ErrorReport.Create(failure, "Report preview", "Preview failed", "The report could not be rendered. The Hidden expression for the tablix 'table2' contains an error.");

        Assert.Equal("Report preview failed. The report could not be rendered. The Hidden expression for the tablix 'table2' contains an error.", report.Message);
        Assert.Contains("Summary: The report could not be rendered.", report.Details, StringComparison.Ordinal);
        Assert.Contains("System.FormatException", report.Details, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("[Bug] Report preview: The report could not be rendered."), report.BugReportUrl(), StringComparison.Ordinal);
    }

    [Fact]
    public void Reporting_without_a_handler_does_nothing()
    {
        var previous = ErrorReporting.Handler;
        try
        {
            ErrorReporting.Handler = null;
            ErrorReporting.Show(new InvalidOperationException("x"));

            Exception? seen = null;
            ErrorReporting.Handler = (exception, _, _, _) => seen = exception;
            var failure = new InvalidOperationException("y");
            ErrorReporting.Show(failure, "op");
            Assert.Same(failure, seen);
        }
        finally
        {
            ErrorReporting.Handler = previous;
        }
    }
}
