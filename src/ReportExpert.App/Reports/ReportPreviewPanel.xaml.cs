using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using ReportExpert.Common;
using ReportExpert.Reporting.Design;

namespace ReportExpert.App.Reports;

/// <summary>
/// The report preview hosted in the shared side panel: asks for the parameters, renders a PDF into an
/// embedded viewer, and exports PDF, Excel or Word with the same values. Pop out opens it in a window.
/// </summary>
public partial class ReportPreviewPanel : System.Windows.Controls.UserControl
{
    private IReportDesignService? _service;
    private ReportDesignModel? _report;
    private IReadOnlyList<DesignParameter> _visibleParameters = [];
    private Dictionary<string, string?> _values = [];
    private bool _viewerReady;
    private string? _currentPdf;

    public ReportPreviewPanel()
    {
        InitializeComponent();
        Unloaded += (_, _) => TryDelete(_currentPdf);
    }

    /// <summary>Asks for parameter values, then renders. False when the user cancels the filter.</summary>
    public async Task<bool> OpenAsync(IReportDesignService service, ReportDesignModel report, Dictionary<string, string?> defaults)
    {
        var values = await ReportPreviewWindow.AskAsync(service, report, defaults, Window.GetWindow(this));
        if (values is null)
        {
            return false;
        }

        _service = service;
        _report = report;
        _values = values;
        _visibleParameters = report.Parameters.Where(parameter => !parameter.Hidden).ToArray();
        FilterButton.Visibility = _visibleParameters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSummary();
        if (!_viewerReady)
        {
            await InitViewerAsync();
        }

        await RenderAsync();
        return true;
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
        if (_service is null || _report is null)
        {
            return;
        }

        SetBusy(true, "Rendering…");
        try
        {
            var output = await _service.RenderAsync(_report.Id, _values, ReportOutputFormat.Pdf, CancellationToken.None);
            var path = Path.Combine(AppConstants.GetTempDirectory(AppConstants.TempPreviewFolderName), $"{Path.GetFileNameWithoutExtension(output.FileName)}-{Guid.NewGuid():N}.pdf");
            await File.WriteAllBytesAsync(path, output.Content);
            var previous = _currentPdf;
            _currentPdf = path;
            if (_viewerReady && PdfViewer.CoreWebView2 is not null)
            {
                Fallback.Visibility = Visibility.Collapsed;
                PdfViewer.Visibility = Visibility.Visible;
                PdfViewer.CoreWebView2.Navigate(new Uri(path).AbsoluteUri);
            }
            else
            {
                PdfViewer.Visibility = Visibility.Collapsed;
                Fallback.Visibility = Visibility.Visible;
                Fallback.Text = "The embedded viewer needs the Microsoft Edge WebView2 Runtime. The report rendered; use Export to save it.";
            }

            TryDelete(previous);
            SetBusy(false, $"Rendered {DateTime.Now:HH:mm:ss} · {output.Content.Length / 1024.0:0.#} KB");
        }
        catch (Exception exception)
        {
            SetBusy(false, Shell.ErrorReporter.ShowPreviewFailure(exception, "Report preview"));
        }
    }

    private async void OnFilter(object sender, RoutedEventArgs e)
    {
        if (_service is null || _report is null)
        {
            return;
        }

        var values = await ReportPreviewWindow.AskAsync(_service, _report, _values, Window.GetWindow(this));
        if (values is not null)
        {
            _values = values;
            UpdateSummary();
            await RenderAsync();
        }
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

    private void OnPopOut(object sender, RoutedEventArgs e)
    {
        if (_service is not null && _report is not null)
        {
            new ReportPreviewWindow(_service, _report, new Dictionary<string, string?>(_values)) { Owner = Window.GetWindow(this) }.Show();
        }
    }

    private void OnExportMenu(object sender, RoutedEventArgs e) => ExportMenu.Open(ExportButton, ExportAsync);

    private async Task ExportAsync(ReportOutputFormat format)
    {
        if (_service is null || _report is null)
        {
            return;
        }

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
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
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
