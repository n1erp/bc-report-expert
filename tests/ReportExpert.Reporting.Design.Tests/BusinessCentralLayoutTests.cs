using System.Xml.Linq;

namespace ReportExpert.Reporting.Design.Tests;

public sealed class BusinessCentralLayoutTests
{
    private static readonly XNamespace Rdl = "http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition";

    [Fact]
    public async Task OpeningABusinessCentralLayoutKeepsItsDatasetAndCode()
    {
        using var fixture = new DesignFixture();
        var model = await fixture.Service.OpenFileAsync(fixture.WriteLayout(), CancellationToken.None);

        var dataset = Assert.Single(model.DataSets);
        Assert.Equal("DataSet_Result", dataset.Name);
        Assert.Equal(ReportDataCommand.BusinessCentralKey, dataset.ProviderKey);
        Assert.Contains(dataset.Fields, field => field.Name == "BalanceLCY_Customer" && field.DataType == "System.Decimal");
        Assert.True(model.HasCustomCode);
        Assert.NotNull(model.SourcePath);
    }

    [Fact]
    public async Task EditingAndSavingPreservesTheBusinessCentralContract()
    {
        using var fixture = new DesignFixture();
        var path = fixture.WriteLayout();
        var model = await fixture.Service.OpenFileAsync(path, CancellationToken.None);

        model = await fixture.Service.AddColumnAsync(model.Id, "CustomerTable", "CompanyName", CancellationToken.None);
        Assert.Equal(2, model.Revision);
        Assert.True(model.CanUndo);

        var written = await fixture.Service.SaveToFileAsync(model.Id, null, CancellationToken.None);
        Assert.Equal(Path.GetFullPath(path), written);
        Assert.True(File.Exists(path + ".bak"));

        var saved = XDocument.Load(path);
        var dataset = saved.Descendants(Rdl + "DataSet").Single();
        Assert.Equal("DataSet_Result", dataset.Attribute("Name")?.Value);
        Assert.Equal("", dataset.Element(Rdl + "Query")!.Element(Rdl + "CommandText")!.Value);
        Assert.Equal("DataSource", dataset.Element(Rdl + "Query")!.Element(Rdl + "DataSourceName")!.Value);
        Assert.Contains("Public Function SetData", saved.Descendants(Rdl + "Code").Single().Value, StringComparison.Ordinal);
        Assert.Equal("SQL", saved.Descendants(Rdl + "DataProvider").Single().Value);
        Assert.Equal(5, saved.Descendants(Rdl + "TablixColumn").Count());
    }

    [Fact]
    public async Task UndoRestoresThePreviousRevision()
    {
        using var fixture = new DesignFixture();
        var model = await fixture.Service.OpenFileAsync(fixture.WriteLayout(), CancellationToken.None);
        var columns = model.Items.Single(item => item.Name == "CustomerTable").Columns!.Count;

        model = await fixture.Service.AddColumnAsync(model.Id, "CustomerTable", "CompanyName", CancellationToken.None);
        Assert.Equal(columns + 1, model.Items.Single(item => item.Name == "CustomerTable").Columns!.Count);

        model = await fixture.Service.StepAsync(model.Id, -1, CancellationToken.None);
        Assert.Equal(columns, model.Items.Single(item => item.Name == "CustomerTable").Columns!.Count);
        Assert.True(model.CanRedo);
    }

    [Fact]
    public async Task ReopeningTheSameFileReusesTheLinkedReportAndPicksUpOutsideChanges()
    {
        using var fixture = new DesignFixture();
        var path = fixture.WriteLayout();
        var first = await fixture.Service.OpenFileAsync(path, CancellationToken.None);

        var again = await fixture.Service.OpenFileAsync(path, CancellationToken.None);
        Assert.Equal(first.Id, again.Id);
        Assert.Equal(first.Revision, again.Revision);

        File.WriteAllText(path, DesignFixture.BusinessCentralLayout.Replace("Balance (LCY)", "Balance", StringComparison.Ordinal));
        var reloaded = await fixture.Service.OpenFileAsync(path, CancellationToken.None);
        Assert.Equal(first.Id, reloaded.Id);
        Assert.Equal(first.Revision + 1, reloaded.Revision);
        Assert.Single(await fixture.Service.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StrictPolicyRefusesCustomCode()
    {
        using var fixture = new DesignFixture(ReportSafetyPolicy.Strict);
        var error = await Assert.ThrowsAsync<ReportDesignException>(() => fixture.Service.OpenFileAsync(fixture.WriteLayout(), CancellationToken.None));
        Assert.Equal(ReportCodes.UnsafeDefinition, error.Message);
    }

    [Fact]
    public async Task ProviderCommandsFromSafiPayAndBusinessCentralResolve()
    {
        Assert.Equal("src.employees", ReportDataCommand.ProviderKey("safipay:src.employees"));
        Assert.Equal("bc", ReportDataCommand.ProviderKey("usp_GetTestData"));
        Assert.Equal("", ReportDataCommand.For("bc"));
        Assert.Equal("rex:x", ReportDataCommand.For("x"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TemplatesInstallOnce()
    {
        using var fixture = new DesignFixture();
        var first = await fixture.Service.InstallTemplatesAsync(CancellationToken.None);
        Assert.Single(first.Installed);
        var second = await fixture.Service.InstallTemplatesAsync(CancellationToken.None);
        Assert.Empty(second.Installed);
        Assert.Single(second.Existing);
    }
}

public sealed class LegacyWorkspaceImportTests
{
    [Fact]
    public async Task AFolderWorkspaceFromRex30IsImportedOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), "rex-legacy", Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "workspace");
        var id = Guid.NewGuid();
        var folder = Path.Combine(workspace, id.ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "versions"));
        var layout = ReportTemplates.Read(ReportTemplates.All[0]);
        File.WriteAllText(Path.Combine(folder, "report.rdlc"), layout);
        File.WriteAllText(Path.Combine(folder, "versions", "1.rdlc"), layout);
        File.WriteAllText(Path.Combine(folder, "meta.json"), "{\"Name\":\"Old report\",\"Revision\":2,\"UpdatedAt\":\"2026-11-01T10:00:00+00:00\",\"UpdatedBy\":\"me\",\"Origin\":\"import\",\"SourcePath\":null}");

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddReportDesign(options => options.StoragePath = workspace);
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var service = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IReportDesignService>(provider);

        var list = await service.ListAsync(CancellationToken.None);
        var report = Assert.Single(list);
        Assert.Equal("Old report", report.Name);
        Assert.Equal(2, report.Revision);
        var model = await service.OpenAsync(id, null, CancellationToken.None);
        Assert.True(model!.CanUndo);
        Assert.True(File.Exists(Path.Combine(workspace, "rex.db")));
    }
}
