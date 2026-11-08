using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ReportExpert.Reporting.Design.Tests;

/// <summary>A design service over a throw-away workspace folder.</summary>
internal sealed class DesignFixture : IDisposable
{
    private readonly ServiceProvider provider;

    public DesignFixture(ReportSafetyPolicy policy = ReportSafetyPolicy.Trusted)
    {
        Root = Path.Combine(Path.GetTempPath(), "rex-design-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddReportDesign(options =>
        {
            options.StoragePath = Path.Combine(Root, "workspace");
            options.Policy = policy;
        });
        provider = services.BuildServiceProvider();
        Service = provider.GetRequiredService<IReportDesignService>();
    }

    public string Root { get; }

    public IReportDesignService Service { get; }

    public static string BusinessCentralLayout =>
        ReportTemplates.Read(ReportTemplates.All.Single(template => template.FileName == "bc-customer-list.rdlc"));

    public string WriteLayout(string name = "CustomerList.rdlc", string? xml = null)
    {
        var path = Path.Combine(Root, name);
        File.WriteAllText(path, xml ?? BusinessCentralLayout);
        return path;
    }

    public void Dispose()
    {
        provider.Dispose();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
