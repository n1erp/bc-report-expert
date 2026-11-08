using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ReportExpert.Reporting.Design.Tests;

/// <summary>Settings › Preview › Sample rows drives the Business Central sample data (it was fixed at 25).</summary>
public sealed class SampleRowsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "rex-sample-rows", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider services;

    public SampleRowsTests()
    {
        services = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddReportDesign(options => options.StoragePath = root)
            .BuildServiceProvider();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(250)]
    public async Task Sample_data_has_the_configured_row_count(int rows)
    {
        services.GetRequiredService<IOptions<ReportingOptions>>().Value.SampleRows = rows;

        var table = await Read();

        Assert.Equal(rows, table.Rows.Count);
    }

    [Fact]
    public async Task A_changed_setting_applies_to_the_next_read()
    {
        var options = services.GetRequiredService<IOptions<ReportingOptions>>().Value;
        options.SampleRows = 10;
        Assert.Equal(10, (await Read()).Rows.Count);

        options.SampleRows = 60;
        Assert.Equal(60, (await Read()).Rows.Count);
    }

    [Fact]
    public async Task Out_of_range_values_are_clamped()
    {
        var options = services.GetRequiredService<IOptions<ReportingOptions>>().Value;
        options.SampleRows = 0;
        Assert.Single((await Read()).Rows);

        options.SampleRows = 100_000;
        Assert.Equal(ReportingOptions.MaxSampleRows, (await Read()).Rows.Count);
    }

    [Fact]
    public void Image_fields_get_a_visible_placeholder_picture()
    {
        using var table = BusinessCentralDataReader.Sample("DataSet_Result", [new ReportField("Picture_CompanyInfo", "System.Byte[]")], 1);
        var bytes = Assert.IsType<byte[]>(table.Rows[0][0]);

        // A PNG, 240×180 (width and height are big-endian at bytes 16–23 of the IHDR chunk).
        Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, bytes[..4]);
        Assert.Equal(240, (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19]);
        Assert.Equal(180, (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23]);
    }

    [Fact]
    public void Default_is_unchanged_for_hosts_without_settings() =>
        Assert.Equal(25, new ReportingOptions().SampleRows);

    private async Task<System.Data.DataTable> Read()
    {
        var provider = services.GetServices<IReportDataProvider>().OfType<IReportDataSetReader>()
            .Single(item => item is BusinessCentralDataProvider);
        var request = new ReportDataSetRequest(Guid.NewGuid(), "DataSet_Result", [new ReportField("No_Customer", "System.String"), new ReportField("Balance", "System.Decimal")]);
        return await provider.ReadAsync(request, new Dictionary<string, string?>(), 5000, CancellationToken.None);
    }

    public void Dispose()
    {
        services.Dispose();
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
