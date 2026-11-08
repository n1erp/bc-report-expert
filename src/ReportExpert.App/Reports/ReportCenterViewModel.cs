using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReportExpert.Reporting.Design;

namespace ReportExpert.App.Reports;

/// <summary>
/// The REX V3 report center, ported from SafiPay's report list and designer side panels
/// (<c>ReportListView</c>, <c>ReportDataPanel</c>, <c>ReportParametersPanel</c>, <c>ReportExplorerPanel</c>,
/// <c>ReportImagesPanel</c>). Every operation goes through <see cref="IReportDesignService"/>, so the report
/// center gets SafiPay's revisions, undo/redo, validation and export, applied to Business Central RDLC files.
/// </summary>
public sealed partial class ReportCenterViewModel : ObservableObject
{
    private readonly IReportDesignService _service;
    private readonly Func<Window?> _owner;
    private readonly List<ReportListItem> _all = [];

    public ReportCenterViewModel(IReportDesignService service, Func<Window?> owner)
    {
        _service = service;
        _owner = owner;
    }

    /// <summary>Raised when the user asks to edit a linked RDLC in the visual layout designer.</summary>
    public event Action<string>? OpenInDesignerRequested;

    public ObservableCollection<ReportListItem> Reports { get; } = [];

    public ObservableCollection<string> WorkspaceLayouts { get; } = [];

    public ObservableCollection<DesignDataSet> DataSets { get; } = [];

    public ObservableCollection<DesignParameter> Parameters { get; } = [];

    public ObservableCollection<DesignExplorerNode> Explorer { get; } = [];

    public ObservableCollection<DesignImage> Images { get; } = [];

    public ObservableCollection<ReportRevisionEntry> Revisions { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    public ObservableCollection<DesignItem> DataRegions { get; } = [];

    public IReadOnlyList<string> ParameterTypes { get; } = ["String", "Integer", "Float", "DateTime", "Boolean"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport), nameof(Title), nameof(SourceText), nameof(DataFileText), nameof(CanSaveToFile), nameof(CanOpenInDesigner))]
    private ReportDesignModel? _current;

    [ObservableProperty]
    private ReportListItem? _selectedReport;

    [ObservableProperty]
    private DesignDataSet? _selectedDataSet;

    [ObservableProperty]
    private DesignItem? _selectedRegion;

    [ObservableProperty]
    private DesignField? _selectedField;

    [ObservableProperty]
    private DataView? _preview;

    [ObservableProperty]
    private string _previewSummary = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _status = "Open an RDLC layout, or install a starter layout, to begin.";

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _newParameterName = string.Empty;

    [ObservableProperty]
    private string _newParameterType = "String";

    [ObservableProperty]
    private string _newParameterPrompt = string.Empty;

    public bool HasReport => Current is not null;

    public string Title => Current?.Name ?? "No report selected";

    public string SourceText => Current?.SourcePath ?? "Workspace only — use Save as… to write an RDLC file";

    public string DataFileText => Current?.DataFile is { } file
        ? Path.GetFileName(file)
        : "Sample data (attach a Business Central XML, JSON or CSV file for real rows)";

    public bool CanSaveToFile => Current?.SourcePath is not null;

    public bool CanOpenInDesigner => Current?.SourcePath is not null;

    public int ReportCount => Reports.Count;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedReportChanged(ReportListItem? value)
    {
        if (value is not null && value.Id != Current?.Id)
        {
            _ = RunAsync(async () => Show(await _service.OpenAsync(value.Id, null, CancellationToken.None)));
        }
    }

    partial void OnSelectedDataSetChanged(DesignDataSet? value)
    {
        Preview = null;
        PreviewSummary = string.Empty;
        if (value is not null)
        {
            _ = LoadPreviewAsync(value.Name);
        }
    }

    public Task InitializeAsync() => RunAsync(RefreshListAsync);

    /// <summary>The RDLC layouts of the open AL workspace, offered for one-click import.</summary>
    public void SetWorkspaceLayouts(IEnumerable<string> layoutPaths)
    {
        WorkspaceLayouts.Clear();
        foreach (var path in layoutPaths.Where(path => path.EndsWith(".rdlc", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".rdl", StringComparison.OrdinalIgnoreCase)))
        {
            WorkspaceLayouts.Add(path);
        }
    }

    /// <summary>Opens a layout file (from the workspace, the Home view or a dialog) in the report center.</summary>
    public Task OpenPathAsync(string path) => RunAsync(async () =>
    {
        Show(await _service.OpenFileAsync(path, CancellationToken.None));
        await RefreshListAsync();
        Succeed($"Opened {Path.GetFileName(path)}.");
    });

    [RelayCommand]
    private Task RefreshAsync() => RunAsync(async () =>
    {
        await RefreshListAsync();
        if (Current is not null)
        {
            Show(await _service.OpenAsync(Current.Id, null, CancellationToken.None));
        }
    });

    [RelayCommand]
    private Task OpenFileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open RDLC layout",
            Filter = "RDLC / RDL layouts (*.rdlc;*.rdl)|*.rdlc;*.rdl|All files (*.*)|*.*",
        };
        return ShowDialog(dialog) ? OpenPathAsync(dialog.FileName) : Task.CompletedTask;
    }

    [RelayCommand]
    private Task OpenWorkspaceLayoutAsync(string? path) =>
        string.IsNullOrWhiteSpace(path) ? Task.CompletedTask : OpenPathAsync(path);

    [RelayCommand]
    private Task NewReportAsync() => RunAsync(async () =>
    {
        var name = "New report " + DateTime.Now.ToString("yyyy-MM-dd HHmm", System.Globalization.CultureInfo.InvariantCulture);
        Show(await _service.CreateAsync(name, CancellationToken.None));
        await RefreshListAsync();
        Succeed(ReportMessages.For(ReportCodes.Created));
    });

    [RelayCommand]
    private Task InstallTemplatesAsync() => RunAsync(async () =>
    {
        var result = await _service.InstallTemplatesAsync(CancellationToken.None);
        await RefreshListAsync();
        Succeed(result.Installed.Count > 0
            ? ReportMessages.For(ReportCodes.TemplatesInstalled) + " " + string.Join(", ", result.Installed)
            : ReportMessages.For(ReportCodes.TemplatesUpToDate));
    });

    [RelayCommand]
    private Task SaveToFileAsync() => WithReportAsync(async report =>
    {
        var path = await _service.SaveToFileAsync(report.Id, null, CancellationToken.None);
        Show(await _service.OpenAsync(report.Id, null, CancellationToken.None));
        Succeed($"Saved to {path}. The previous version is kept as {Path.GetFileName(path)}.bak.");
    });

    [RelayCommand]
    private Task SaveAsAsync() => WithReportAsync(async report =>
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save RDLC layout",
            Filter = "RDLC layout (*.rdlc)|*.rdlc",
            FileName = (report.SourcePath is null ? report.Name : Path.GetFileName(report.SourcePath)).Replace(".rdlc", "", StringComparison.OrdinalIgnoreCase) + ".rdlc",
            InitialDirectory = report.SourcePath is null ? null : Path.GetDirectoryName(report.SourcePath),
        };
        if (!ShowDialog(dialog))
        {
            return;
        }

        var path = await _service.SaveToFileAsync(report.Id, dialog.FileName, CancellationToken.None);
        Show(await _service.OpenAsync(report.Id, null, CancellationToken.None));
        await RefreshListAsync();
        Succeed($"Saved to {path}.");
    });

    [RelayCommand]
    private Task UndoAsync() => WithReportAsync(async report =>
        Show(await _service.StepAsync(report.Id, -1, CancellationToken.None)));

    [RelayCommand]
    private Task RedoAsync() => WithReportAsync(async report =>
        Show(await _service.StepAsync(report.Id, 1, CancellationToken.None)));

    [RelayCommand]
    private Task RemoveAsync() => WithReportAsync(async report =>
    {
        var answer = System.Windows.MessageBox.Show(
            $"Remove \"{report.Name}\" and its revision history from the report center?\n\nThe RDLC file on disk is not deleted.",
            "Report Expert",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await _service.DeleteAsync(report.Id, CancellationToken.None);
        Show(null);
        await RefreshListAsync();
        Succeed(ReportMessages.For(ReportCodes.Deleted));
    });

    [RelayCommand]
    private Task AttachDataAsync() => WithReportAsync(async report =>
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Attach Business Central data",
            Filter = "Report data (*.xml;*.json;*.csv)|*.xml;*.json;*.csv|All files (*.*)|*.*",
        };
        if (!ShowDialog(dialog))
        {
            return;
        }

        Show(await _service.AttachDataAsync(report.Id, dialog.FileName, CancellationToken.None));
        Succeed($"Previews and exports now read {Path.GetFileName(dialog.FileName)}.");
    });

    [RelayCommand]
    private Task DetachDataAsync() => WithReportAsync(async report =>
    {
        Show(await _service.AttachDataAsync(report.Id, null, CancellationToken.None));
        Succeed("Previews use sample data again.");
    });

    [RelayCommand]
    private Task ExportAsync(string? format) => WithReportAsync(async report =>
    {
        var kind = format switch
        {
            "Excel" => ReportOutputFormat.Excel,
            "Word" => ReportOutputFormat.Word,
            _ => ReportOutputFormat.Pdf,
        };
        var output = await _service.RenderAsync(report.Id, Defaults(report), kind, CancellationToken.None);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export report",
            FileName = output.FileName,
            Filter = kind switch
            {
                ReportOutputFormat.Excel => "Excel workbook (*.xlsx)|*.xlsx",
                ReportOutputFormat.Word => "Word document (*.docx)|*.docx",
                _ => "PDF document (*.pdf)|*.pdf",
            },
        };
        if (!ShowDialog(dialog))
        {
            return;
        }

        await File.WriteAllBytesAsync(dialog.FileName, output.Content);
        Succeed($"Exported {Path.GetFileName(dialog.FileName)} ({output.Content.Length / 1024.0:0.#} KB).");
    });

    [RelayCommand]
    private void OpenInDesigner()
    {
        if (Current?.SourcePath is { } path)
        {
            OpenInDesignerRequested?.Invoke(path);
        }
    }

    [RelayCommand]
    private Task AddColumnAsync() => WithReportAsync(async report =>
    {
        if (SelectedRegion is null || SelectedField is null)
        {
            Fail("Choose a table and a field first.");
            return;
        }

        Show(await _service.AddColumnAsync(report.Id, SelectedRegion.Name, SelectedField.Name, CancellationToken.None));
        Succeed($"Added {SelectedField?.Name ?? "the field"} to {SelectedRegion?.Name ?? "the table"}.");
    });

    [RelayCommand]
    private Task AddTotalAsync() => WithReportAsync(async report =>
    {
        if (SelectedRegion is null)
        {
            Fail("Choose a table first.");
            return;
        }

        Show(await _service.AddTotalAsync(report.Id, SelectedRegion.Name, CancellationToken.None));
        Succeed("Added a totals row.");
    });

    [RelayCommand]
    private Task AddParameterAsync() => WithReportAsync(async report =>
    {
        var name = NewParameterName.Trim();
        Show(await _service.AddParameterAsync(report.Id, name, NewParameterType, string.IsNullOrWhiteSpace(NewParameterPrompt) ? name : NewParameterPrompt.Trim(), CancellationToken.None));
        NewParameterName = string.Empty;
        NewParameterPrompt = string.Empty;
        Succeed($"Added parameter {name}.");
    });

    [RelayCommand]
    private Task RemoveParameterAsync(DesignParameter? parameter) => WithReportAsync(async report =>
    {
        if (parameter is null)
        {
            return;
        }

        Show(await _service.RemoveParameterAsync(report.Id, parameter.Name, CancellationToken.None));
        Succeed($"Removed parameter {parameter.Name}.");
    });

    /// <summary>
    /// Literal parameter defaults for preview and export. Expression defaults (<c>=Month(Today())</c>) and
    /// parameters without a default are left out, so the renderer evaluates them instead of rejecting the text.
    /// </summary>
    private static Dictionary<string, string?> Defaults(ReportDesignModel report) =>
        report.Parameters
            .Where(parameter => parameter.DefaultValue is { } value && !value.TrimStart().StartsWith('='))
            .ToDictionary(parameter => parameter.Name, parameter => parameter.DefaultValue, StringComparer.Ordinal);

    private async Task LoadPreviewAsync(string dataSetName)
    {
        if (Current is not { } report)
        {
            return;
        }

        try
        {
            var data = await _service.PreviewDataAsync(report.Id, dataSetName, CancellationToken.None);
            var table = new DataTable(data.DataSetName);
            foreach (var column in data.Columns)
            {
                table.Columns.Add(column.Replace("_", "__", StringComparison.Ordinal), typeof(string));
            }

            foreach (var row in data.Rows)
            {
                table.Rows.Add(row.Cast<object>().ToArray());
            }

            Preview = table.DefaultView;
            var origin = report.DataFile is null ? "sample rows" : Path.GetFileName(report.DataFile);
            PreviewSummary = $"{data.Rows.Count}{(data.Truncated ? "+" : "")} rows from {origin}";
        }
        catch (Exception exception)
        {
            Preview = null;
            PreviewSummary = ReportMessages.For(exception);
        }
    }

    private async Task RefreshListAsync()
    {
        var items = await _service.ListAsync(CancellationToken.None);
        _all.Clear();
        _all.AddRange(items);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        Reports.Clear();
        foreach (var item in _all.Where(item => query.Length == 0
                     || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || (item.SourcePath?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)))
        {
            Reports.Add(item);
        }

        OnPropertyChanged(nameof(ReportCount));
        if (Current is { } current)
        {
            SelectedReport = Reports.FirstOrDefault(item => item.Id == current.Id);
        }
    }

    private void Show(ReportDesignModel? model)
    {
        var dataSet = SelectedDataSet?.Name;
        Current = model;
        Replace(DataSets, model?.DataSets);
        Replace(Parameters, model?.Parameters);
        Replace(Explorer, model?.Explorer);
        Replace(Images, model?.Images);
        Replace(Warnings, model?.Warnings);
        Replace(DataRegions, model?.Items.Where(item => item.Kind is "Tablix" or "Table" or "Matrix" or "List"));
        SelectedDataSet = DataSets.FirstOrDefault(set => set.Name == dataSet) ?? DataSets.FirstOrDefault();
        var region = SelectedRegion?.Name;
        SelectedRegion = DataRegions.FirstOrDefault(candidate => candidate.Name == region) ?? DataRegions.FirstOrDefault();

        ShowDesigner(model);
        if (model is not null)
        {
            _ = LoadRevisionsAsync(model.Id);
            if (SelectedReport?.Id != model.Id)
            {
                SelectedReport = _all.FirstOrDefault(item => item.Id == model.Id);
            }
        }
        else
        {
            Revisions.Clear();
        }
    }

    private async Task LoadRevisionsAsync(Guid id)
    {
        try
        {
            Replace(Revisions, await _service.RevisionsAsync(id, CancellationToken.None));
        }
        catch (Exception)
        {
            Revisions.Clear();
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T>? items)
    {
        target.Clear();
        foreach (var item in items ?? [])
        {
            target.Add(item);
        }
    }

    private Task WithReportAsync(Func<ReportDesignModel, Task> action, [System.Runtime.CompilerServices.CallerMemberName] string operation = "") =>
        Current is { } report ? RunAsync(() => action(report), operation) : Task.CompletedTask;

    private async Task RunAsync(Func<Task> action, [System.Runtime.CompilerServices.CallerMemberName] string operation = "")
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await action();
        }
        catch (ReportDesignException exception) when (exception.Message is ReportCodes.RenderFailed or ReportCodes.InvalidDefinition or ReportCodes.LoadFailed or ReportCodes.DataUnavailable)
        {
            // The report itself is at fault (an expression, the definition, its data): the developer needs
            // the engine's words and the details, with a way to report it if REX is to blame.
            Fail(Shell.ErrorReporter.ShowPreviewFailure(exception, "Report designer: " + OperationName(operation)));
        }
        catch (ReportDesignException exception)
        {
            // An expected rejection (invalid value, item not found…): the status bar says why.
            Fail(ReportMessages.For(exception));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            // Anything else is a defect: show it with its details and a way to report it.
            Fail(ReportMessages.For(exception));
            Shell.ErrorReporter.Show(exception, "Report designer: " + OperationName(operation));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>"AddGroupCoreAsync" → "Add group core".</summary>
    private static string OperationName(string member)
    {
        var name = member.EndsWith("Async", StringComparison.Ordinal) ? member[..^5] : member;
        var words = System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z0-9])([A-Z])", " $1").Trim();
        return words.Length == 0 ? "Operation" : char.ToUpperInvariant(words[0]) + words[1..].ToLowerInvariant();
    }

    private void Succeed(string message)
    {
        IsError = false;
        Status = message;
    }

    private void Fail(string message)
    {
        IsError = true;
        Status = message;
    }

    private bool ShowDialog(Microsoft.Win32.CommonDialog dialog)
    {
        var owner = _owner();
        return (owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) == true;
    }
}
