using System.Diagnostics;

namespace ReportExpert.Common;

/// <summary>REX is open source: links to its GitHub repository and issue tracker.</summary>
public static class ProjectLinks
{
    /// <summary>The GitHub repository.</summary>
    public const string RepositoryUrl = "https://github.com/n1erp/bc-report-expert";

    /// <summary>The issue list.</summary>
    public const string IssuesUrl = RepositoryUrl + "/issues/";

    /// <summary>The issue template chooser (bug, feature, RDLC compatibility, question, documentation).</summary>
    public const string NewIssueUrl = RepositoryUrl + "/issues/new/choose";

    /// <summary>The licence file.</summary>
    public const string LicenseUrl = RepositoryUrl + "/blob/main/LICENSE";

    /// <summary>Issue form file names under <c>.github/ISSUE_TEMPLATE</c>.</summary>
    public const string BugTemplate = "bug_report.yml";

    public const string FeatureTemplate = "feature_request.yml";

    /// <summary>GitHub rejects very long URLs; prefilled fields are cut to stay well below the limit.</summary>
    public const int MaxUrlLength = 7500;

    /// <summary>
    /// A new-issue URL for an issue form, with the title and form fields (by field id) prefilled.
    /// Field values are truncated so the URL stays under <see cref="MaxUrlLength"/>.
    /// </summary>
    public static string NewIssue(string template, string? title = null, IReadOnlyDictionary<string, string>? fields = null)
    {
        var query = new List<string> { "template=" + Uri.EscapeDataString(template) };
        if (!string.IsNullOrWhiteSpace(title))
        {
            query.Add("title=" + Uri.EscapeDataString(title.Length > 200 ? title[..200] : title));
        }

        var url = RepositoryUrl + "/issues/new?" + string.Join("&", query);
        foreach (var (id, value) in fields ?? new Dictionary<string, string>())
        {
            var room = MaxUrlLength - url.Length - id.Length - 2;
            if (room <= 0 || string.IsNullOrEmpty(value))
            {
                continue;
            }

            var text = value;
            var encoded = Uri.EscapeDataString(text);
            while (encoded.Length > room && text.Length > 0)
            {
                // Cut by characters, then re-encode; keep a marker so the reader knows text is missing.
                text = text[..Math.Max(0, text.Length - Math.Max(64, (encoded.Length - room) / 3))];
                encoded = Uri.EscapeDataString(text + "\n… (truncated: use Copy details and paste the rest)");
            }

            if (encoded.Length <= room)
            {
                url += "&" + id + "=" + encoded;
            }
        }

        return url;
    }

    /// <summary>Opens a URL in the default browser. Returns false when no browser could be started.</summary>
    public static bool Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
