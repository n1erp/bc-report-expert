namespace ReportExpert.Reporting.Design.Tests;

public sealed class RenderTests
{
    [Theory]
    [InlineData(ReportOutputFormat.Excel, "PK")]
    [InlineData(ReportOutputFormat.Word, "PK")]
    public async Task ABusinessCentralLayoutRendersWithSampleData(ReportOutputFormat format, string signature)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        using var fixture = new DesignFixture();
        var model = await fixture.Service.OpenFileAsync(fixture.WriteLayout(), CancellationToken.None);

        var output = await fixture.Service.RenderAsync(model.Id, new Dictionary<string, string?>(), format, CancellationToken.None);

        Assert.True(output.Content.Length > 100);
        Assert.Equal(signature, System.Text.Encoding.ASCII.GetString(output.Content, 0, signature.Length));
    }

    /// <summary>The PDF renderer measures text with GDI+, which ReportViewer only supports on Windows.</summary>
    [Fact]
    public async Task ABusinessCentralLayoutRendersToPdfOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        using var fixture = new DesignFixture();
        var model = await fixture.Service.OpenFileAsync(fixture.WriteLayout(), CancellationToken.None);
        var output = await fixture.Service.RenderAsync(model.Id, new Dictionary<string, string?>(), ReportOutputFormat.Pdf, CancellationToken.None);
        const string signature = "%PDF";
        Assert.Equal(signature, System.Text.Encoding.ASCII.GetString(output.Content, 0, signature.Length));
    }
}
