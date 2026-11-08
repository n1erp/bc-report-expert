using System.Reflection;

namespace ReportExpert.Reporting.Design;

/// <summary>A starter layout shipped with REX, installed into the report center as an editable copy.</summary>
/// <param name="FileName">The RDLC file under <c>Templates</c>.</param>
/// <param name="Name">The report name shown in the report center.</param>
/// <param name="Category">The family it belongs to.</param>
public sealed record ReportTemplate(string FileName, string Name, string Category);

/// <summary>Outcome of installing the starter layouts.</summary>
/// <param name="Installed">Names of the reports added.</param>
/// <param name="Existing">Names already present in the report center, left untouched.</param>
/// <param name="Unavailable">Names whose data sources this installation does not have.</param>
public sealed record ReportTemplateInstall(IReadOnlyList<string> Installed, IReadOnlyList<string> Existing, IReadOnlyList<string> Unavailable);

/// <summary>
/// Starter layouts. They are Business Central RDLC layouts (<c>DataSet_Result</c>, the standard
/// <c>GetData</c>/<c>SetData</c> code block), so they can be copied into an AL extension as they are.
/// Once installed they are ordinary reports.
/// </summary>
public static class ReportTemplates
{
    public static IReadOnlyList<ReportTemplate> All { get; } =
    [
        new("bc-customer-list.rdlc", "Customer list (Business Central)", "Business Central"),
    ];

    /// <summary>The RDLC text of a template.</summary>
    public static string Read(ReportTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var resource = "ReportExpert.Reporting.Design.Templates." + template.FileName;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("The report template " + template.FileName + " is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
