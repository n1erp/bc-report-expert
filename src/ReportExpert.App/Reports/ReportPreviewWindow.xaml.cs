using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using ReportExpert.Common;
using ReportExpert.Reporting.Design;

namespace ReportExpert.App.Reports;

/// <summary>
/// The in-app report preview: renders the report to PDF with the chosen parameter values and shows it in an
/// embedded viewer (WebView2), with Parameters to filter again and Export to PDF, Excel or Word.
/// </summary>
public partial class ReportPreviewWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly IReportDesignService _service;
    private readonly ReportDesignModel _report;
    private readonly IReadOnlyList<DesignParameter> _visibleParameters;
    private Dictionary<string, string?> _values;
    private bool _viewerReady;
    private string? _currentPdf;

    public ReportPreviewWindow(IReportDesignService service, ReportDesignModel report, Dictionary<string, string?> values)
    {
        InitializeComponent();
        _service = service;
        _report = report;
        _values = values;
        _visibleParameters = report.Parameters.Where(parameter => !parameter.Hidden).ToArray();
        Title = $"Preview — {report.Name}";
        ReportTitle.Text = $"Preview — {report.Name}";
        FilterButton.Visibility = _visibleParameters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSummary();
        Loaded += async (_, _) =>
        {
            await InitViewerAsync();
            await RenderAsync();
        };
        Closed += (_, _) => Cleanup();
    }

    /// <summary>
    /// Asks for the parameter values (when the report has visible parameters), then opens the preview.
    /// Returns false when the user cancels the filter.
    /// </summary>
    public static async Task<bool> ShowAsync(IReportDesignService service, ReportDesignModel report, Dictionary<string, string?> defaults, Window? owner)
    {
        var values = await AskAsync(service, report, defaults, owner);
        if (values is null)
        {
            return false;
        }

        var window = new ReportPreviewWindow(service, report, values) { Owner = owner };
        window.Show();
        return true;
    }

    internal static async Task<Dictionary<string, string?>?> AskAsync(IReportDesignService service, ReportDesignModel report, Dictionary<string, string?> current, Window? owner)
    {
        var visible = report.Parameters.Where(parameter => !parameter.Hidden).ToArray();
        if (visible.Length == 0)
        {
            return current;
        }

        IReadOnlyDictionary<string, IReadOnlyList<ParameterOption>> options;
        try
        {
            options = await service.ParameterOptionsAsync(report.Id, CancellationToken.None);
        }
        catch (Exception)
        {
            options = new Dictionary<string, IReadOnlyList<ParameterOption>>();
        }

        var dialog = new ReportParametersDialog(report.Name, visible, options, current) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Values : null;
    }

    private async Task InitViewerAsync()
    {
        try
        {
            var folder = AppConstants.GetAppDataPath(AppConstants.WebView2FolderName);
            Directory.CreateDirectory(folder);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: folder);
            await PdfViewer.EnsureCoreWebView2Async(environment);
            _viewerReady = true;
        }
        catch (Exception)
        {
            _viewerReady = false;
        }
    }

    private async Task RenderAsync()
    {
        SetBusy(true, "Rendering…");
        try
        {
            var output = await _service.RenderAsync(_report.Id, _values, ReportOutputFormat.Pdf, CancellationToken.None);
            var folder = AppConstants.GetTempDirectory(AppConstants.TempPreviewFolderName);
            var path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(output.FileName)}-{Guid.NewGuid():N}.pdf");
            await File.WriteAllBytesAsync(path, output.Content);
            var previous = _currentPdf;
            _currentPdf = path;
            ShowPdf(path);
            TryDelete(previous);
            SetBusy(false, $"Rendered {DateTime.Now:HH:mm:ss} · {output.Content.Length / 1024.0:0.#} KB");
        }
        catch (Exception exception)
        {
            SetBusy(false, Shell.ErrorReporter.ShowPreviewFailure(exception, "Report preview"));
        }
    }

    private void ShowPdf(string path)
    {
        if (_viewerReady && PdfViewer.CoreWebView2 is not null)
        {
            Fallback.Visibility = Visibility.Collapsed;
            PdfViewer.Visibility = Visibility.Visible;
            PdfViewer.CoreWebView2.Navigate(new Uri(path).AbsoluteUri);
            return;
        }

        PdfViewer.Visibility = Visibility.Collapsed;
        Fallback.Visibility = Visibility.Visible;
        Fallback.Text = "The embedded viewer needs the Microsoft Edge WebView2 Runtime. The report rendered; use Export to save it.";
    }

    private async void OnFilter(object sender, RoutedEventArgs e)
    {
        var values = await AskAsync(_service, _report, _values, this);
        if (values is null)
        {
            return;
        }

        _values = values;
        UpdateSummary();
        await RenderAsync();
    }

    /// <summary>F5 renders again with the same values (the toolbar keeps Parameters and Export only).</summary>
    protected override async void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == System.Windows.Input.Key.F5)
        {
            e.Handled = true;
            await RenderAsync();
        }
    }

    private void OnExportMenu(object sender, RoutedEventArgs e) => ExportMenu.Open(ExportButton, ExportAsync);

    private async Task ExportAsync(ReportOutputFormat format)
    {
        SetBusy(true, $"Exporting {format}…");
        try
        {
            var output = await _service.RenderAsync(_report.Id, _values, format, CancellationToken.None);
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export report",
                FileName = output.FileName,
                Filter = ExportMenu.Filter(format),
            };
            if (dialog.ShowDialog(this) != true)
            {
                SetBusy(false, "Export cancelled.");
                return;
            }

            await File.WriteAllBytesAsync(dialog.FileName, output.Content);
            SetBusy(false, $"Exported {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception exception)
        {
            SetBusy(false, Shell.ErrorReporter.ShowPreviewFailure(exception, "Report export"));
        }
    }

    /// <summary>The current parameter values, shown as the Parameters button's tooltip.</summary>
    private void UpdateSummary()
    {
        var values = string.Join("\n", _visibleParameters.Select(parameter =>
            $"{(string.IsNullOrWhiteSpace(parameter.Prompt) ? parameter.Name : parameter.Prompt)}: " +
            (_values.TryGetValue(parameter.Name, out var value) && !string.IsNullOrEmpty(value)
                ? value.Replace(ReportParameterValues.Separator, ',')
                : "default")));
        FilterButton.ToolTip = _visibleParameters.Count == 0
            ? "This report has no parameters."
            : "Change the parameter values and render again\n\n" + values;
    }

    private void SetBusy(bool busy, string message)
    {
        Busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Status.Text = message;
    }

    private void Cleanup()
    {
        try
        {
            PdfViewer.Dispose();
        }
        catch (Exception)
        {
            // Best effort.
        }

        TryDelete(_currentPdf);
    }

    private static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
