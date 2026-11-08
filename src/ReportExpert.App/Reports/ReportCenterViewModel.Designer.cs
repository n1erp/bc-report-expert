using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReportExpert.Reporting.Design;

namespace ReportExpert.App.Reports;

/// <summary>A toolbox entry: the engine kind it places, its label and its icon.</summary>
public sealed record ToolboxEntry(string Kind, string Label, string Icon);

/// <summary>
/// The arrange operations available for a selection, indexed by operation name. The row keys "Size" and
/// "Spacing" say whether any operation of that row applies, so an empty row can be hidden.
/// </summary>
public sealed class ArrangeAvailability(IReadOnlyList<string> operations)
{
    private readonly HashSet<string> _operations = new(operations, StringComparer.Ordinal);

    public bool this[string operation] => operation switch
    {
        "Spacing" => _operations.Contains("DistributeHorizontally"),
        "Any" => _operations.Count > 0,
        _ => _operations.Contains(operation),
    };
}

/// <summary>A toolbox category, collapsible, as in SafiPay's designer toolbox.</summary>
public sealed record ToolboxGroup(string Name, IReadOnlyList<ToolboxEntry> Items);

/// <summary>
/// The REX V3 visual designer state, ported from SafiPay's report designer (toolbar, toolbox, surface,
/// properties and grouping panels). Every gesture becomes one <see cref="IReportDesignService"/> call, so each
/// one is a revision that undo/redo can step through.
/// </summary>
public sealed partial class ReportCenterViewModel
{
    private static readonly IReadOnlyList<ToolboxGroup> AllTools =
    [
        new("Basic items", [T("Textbox", "Text box", "TextT24"), T("Image", "Image", "Image24"), T("Line", "Line", "LineHorizontal124"), T("Rectangle", "Rectangle", "RectangleLandscape24")]),
        new("Data regions", [T("Table", "Table", "Table24"), T("List", "List", "TextBulletListLtr24"), T("Matrix", "Matrix", "Grid24")]),
        new("Comparison", [T("Chart:Column", "Column", "DataBarVertical24"), T("Chart:Bar", "Bar", "DataBarHorizontal24"), T("Chart:StackedColumn", "Stacked column", "DataBarVertical24"), T("Chart:StackedBar", "Stacked bar", "DataBarHorizontal24"), T("Chart:PercentColumn", "100% column", "DataBarVertical24"), T("Chart:PercentBar", "100% bar", "DataBarHorizontal24"), T("Chart:RangeColumn", "Range column", "DataBarVertical24"), T("Chart:RangeBar", "Range bar", "DataBarHorizontal24")]),
        new("Proportion", [T("Chart:Pie", "Pie", "DataPie24"), T("Chart:ExplodedPie", "Exploded pie", "DataPie24"), T("Chart:Doughnut", "Doughnut", "DataPie24"), T("Chart:Funnel", "Funnel", "DataFunnel24"), T("Chart:Pyramid", "Pyramid", "Triangle24")]),
        new("Distribution", [T("Chart:Line", "Line", "DataLine24"), T("Chart:SmoothLine", "Smooth line", "DataLine24"), T("Chart:SteppedLine", "Stepped line", "DataLine24"), T("Chart:LineMarkers", "Line with markers", "DataLine24"), T("Chart:Area", "Area", "DataArea24"), T("Chart:SmoothArea", "Smooth area", "DataArea24"), T("Chart:StackedArea", "Stacked area", "DataArea24"), T("Chart:PercentArea", "100% area", "DataArea24"), T("Chart:RangeArea", "Range area", "DataArea24"), T("Chart:SmoothRangeArea", "Smooth range area", "DataArea24"), T("Chart:Scatter", "Scatter", "DataScatter24"), T("Chart:Bubble", "Bubble", "DataScatter24"), T("Chart:Polar", "Polar", "Target24"), T("Chart:Radar", "Radar", "Target24")]),
        new("Bands", [T("Header", "Page header (on/off)", "DocumentHeader24"), T("Footer", "Page footer (on/off)", "DocumentFooter24")]),
    ];

    /// <summary>The Bands tiles for the open report: "Add …" when the band is off, "Remove …" when it is on.</summary>
    private IReadOnlyList<ToolboxGroup> CurrentTools() =>
        AllTools.Select(group => group.Name != "Bands" ? group : group with
        {
            Items =
            [
                T("Header", Current?.HasHeader == true ? "Remove page header" : "Add page header", "DocumentHeader24"),
                T("Footer", Current?.HasFooter == true ? "Remove page footer" : "Add page footer", "DocumentFooter24"),
            ],
        }).ToList();

    private bool? _toolboxHeader;
    private bool? _toolboxFooter;

    /// <summary>Rebuilds the toolbox when the band state changed, keeping the search filter.</summary>
    private void RefreshBandTools()
    {
        if (_toolboxHeader == Current?.HasHeader && _toolboxFooter == Current?.HasFooter)
        {
            return;
        }

        _toolboxHeader = Current?.HasHeader;
        _toolboxFooter = Current?.HasFooter;
        OnToolboxSearchChanged(ToolboxSearch);
    }

    /// <summary>Arrange operations offered by the Align and Layout menus.</summary>
    public IReadOnlyList<string> AlignOperations { get; } = ["AlignLeft", "AlignCenter", "AlignRight", "AlignTop", "AlignMiddle", "AlignBottom", "SameSize", "SameWidth", "SameHeight", "CenterHorizontally", "CenterVertically", "FitWidth", "AlignToGrid", "SizeToGrid"];

    public IReadOnlyList<string> LayoutOperations { get; } = ["BringToFront", "SendToBack", "BringForward", "SendBackward"];

    public ObservableCollection<ToolboxGroup> Toolbox { get; } = new(ArrangeTools(AllTools, string.Empty, DesignerPanelState.WidgetsAlphabetical));

    /// <summary>Widgets as one alphabetical list (A–Z) instead of grouped by category. Remembered between sessions.</summary>
    [ObservableProperty]
    private bool _isToolboxAlphabetical = DesignerPanelState.WidgetsAlphabetical;

    public ObservableCollection<DesignPropertySection> PropertySections { get; } = [];

    public ObservableCollection<DesignGroup> RowGroups { get; } = [];

    public ObservableCollection<DesignGroup> ColumnGroups { get; } = [];

    /// <summary>
    /// Which Layout buttons apply to the current selection, so the Properties pane offers only those
    /// (for example "make same size" needs two items, "spacing" three). Bind with <c>Arrangements[Op]</c>.
    /// </summary>
    public ArrangeAvailability Arrangements => new(ReportArrangeOperations.For(
        Current is { } report ? SelectedItems.Count(name => report.Items.Any(item => item.Name == name)) : 0));

    /// <summary>Items selected on the surface; the first is the primary selection.</summary>
    public ObservableCollection<string> SelectedItems { get; } = [];

    [ObservableProperty]
    private string _toolboxSearch = string.Empty;

    /// <summary>Whether the properties panel is shown. Selecting an item on the page opens it.</summary>
    [ObservableProperty]
    private bool _isPropertiesOpen = true;

    [RelayCommand]
    private void ToggleProperties() => IsPropertiesOpen = !IsPropertiesOpen;

    /// <summary>Whether the widgets (toolbox) panel is shown.</summary>
    [ObservableProperty]
    private bool _isToolboxOpen = true;

    [RelayCommand]
    private void ToggleToolbox() => IsToolboxOpen = !IsToolboxOpen;

    /// <summary>
    /// True while the Design tab is shown. The workspace editor shows its side bar (Properties, Rex, Report items)
    /// only then: the other tabs have no use for it.
    /// </summary>
    [ObservableProperty]
    private bool _isDesignTabActive = true;

    /// <summary>True while the Layout tab (RDLC XML) is shown, so the XML is loaded and kept current.</summary>
    [ObservableProperty]
    private bool _isXmlView;

    /// <summary>The RDLC XML of the current revision, as it would be saved.</summary>
    [ObservableProperty]
    private string _xmlText = string.Empty;

    public string LayoutSummary => Current is { } report
        ? $"{report.Items.Count} items · {report.DataSets.Count} dataset(s) · {report.Parameters.Count} parameter(s) · revision {report.Revision}"
        : string.Empty;

    partial void OnIsXmlViewChanged(bool value)
    {
        if (value)
        {
            _ = LoadXmlAsync();
        }
    }

    private async Task LoadXmlAsync()
    {
        if (Current is not { } report)
        {
            XmlText = string.Empty;
            return;
        }

        try
        {
            var output = await _service.DefinitionAsync(report.Id, CancellationToken.None);
            XmlText = System.Text.Encoding.UTF8.GetString(output.Content).TrimStart('\uFEFF');
        }
        catch (Exception exception)
        {
            XmlText = "<!-- " + ReportMessages.For(exception) + " -->";
        }
    }

    [RelayCommand]
    private void CopyXml()
    {
        if (XmlText.Length > 0)
        {
            System.Windows.Clipboard.SetText(XmlText);
            Succeed("RDLC XML copied to the clipboard.");
        }
    }

    /// <summary>Saves the current layout as an .rdlc file anywhere, without changing the linked file.</summary>
    [RelayCommand]
    private Task ExportRdlcAsync() => WithReportAsync(async report =>
    {
        var output = await _service.DefinitionAsync(report.Id, CancellationToken.None);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export RDLC",
            Filter = "RDLC layout (*.rdlc)|*.rdlc|XML file (*.xml)|*.xml",
            FileName = report.SourcePath is null ? output.FileName : Path.GetFileName(report.SourcePath),
        };
        if (!ShowDialog(dialog))
        {
            return;
        }

        await File.WriteAllBytesAsync(dialog.FileName, output.Content);
        Succeed($"Exported {Path.GetFileName(dialog.FileName)}.");
    });

    /// <summary>File name shown in the designer toolbar when hosted in the workspace editor.</summary>
    [ObservableProperty]
    private string _documentTitle = string.Empty;

    /// <summary>The workspace editor's Save, so dirty tracking stays in one place. Falls back to Save to file.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveSaveCommand))]
    private System.Windows.Input.ICommand? _hostSaveCommand;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHostReload))]
    private System.Windows.Input.ICommand? _hostReloadCommand;

    /// <summary>Opens and closes the Rex Copilot panel beside the designer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCopilot))]
    private System.Windows.Input.ICommand? _hostCopilotCommand;

    public System.Windows.Input.ICommand EffectiveSaveCommand => HostSaveCommand ?? SaveToFileCommand;

    public bool HasHostReload => HostReloadCommand is not null;

    public bool HasCopilot => HostCopilotCommand is not null;

    public bool HasDocumentTitle => DocumentTitle.Length > 0;

    partial void OnDocumentTitleChanged(string value) => OnPropertyChanged(nameof(HasDocumentTitle));

    /// <summary>Hooks the designer up to the workspace editor that hosts it.</summary>
    public void AttachHost(string title, System.Windows.Input.ICommand save, System.Windows.Input.ICommand reload, System.Windows.Input.ICommand copilot)
    {
        DocumentTitle = title;
        HostSaveCommand = save;
        HostReloadCommand = reload;
        HostCopilotCommand = copilot;
    }

    [ObservableProperty]
    private double _zoom = 1.0;

    [ObservableProperty]
    private bool _showGrid = true;

    [ObservableProperty]
    private string _groupField = string.Empty;

    /// <summary>Raised after every model change so the surface redraws.</summary>
    public event Action? DesignChanged;

    public string ZoomText => $"{Zoom * 100:0}%";

    public string SelectedItem => Current?.SelectedItem ?? "Report";

    public DesignItem? SelectedDesignItem => Current?.Items.FirstOrDefault(item => item.Name == Current.SelectedItem);

    public bool HasGroupTarget => SelectedTablix is not null;

    partial void OnZoomChanged(double value)
    {
        OnPropertyChanged(nameof(ZoomText));
        DesignChanged?.Invoke();
    }

    partial void OnShowGridChanged(bool value) => DesignChanged?.Invoke();

    partial void OnToolboxSearchChanged(string value)
    {
        Toolbox.Clear();
        foreach (var group in ArrangeTools(CurrentTools(), value, IsToolboxAlphabetical))
        {
            Toolbox.Add(group);
        }
    }

    partial void OnIsToolboxAlphabeticalChanged(bool value)
    {
        DesignerPanelState.WidgetsAlphabetical = value;
        OnToolboxSearchChanged(ToolboxSearch);
    }

    /// <summary>
    /// Filters the widgets by the search text and, for A–Z, flattens them into one list sorted by name.
    /// Names used twice (the Line shape and the Line chart) get "chart" added so the flat list stays clear.
    /// </summary>
    private static IReadOnlyList<ToolboxGroup> ArrangeTools(IReadOnlyList<ToolboxGroup> groups, string search, bool alphabetical)
    {
        var query = search.Trim();
        bool Matches(ToolboxEntry item) => query.Length == 0 || item.Label.Contains(query, StringComparison.OrdinalIgnoreCase);

        if (!alphabetical)
        {
            return groups
                .Select(group => group with { Items = group.Items.Where(Matches).ToArray() })
                .Where(group => group.Items.Count > 0)
                .ToList();
        }

        var all = groups.SelectMany(group => group.Items).ToList();
        var repeated = all.GroupBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .Where(names => names.Count() > 1).Select(names => names.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = all
            .Select(item => repeated.Contains(item.Label) && item.Kind.StartsWith("Chart:", StringComparison.Ordinal)
                ? item with { Label = item.Label + " chart" }
                : item)
            .Where(Matches)
            .OrderBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return items.Length == 0 ? [] : [new ToolboxGroup("All widgets", items)];
    }

    /// <summary>Called by <see cref="Show"/> after a model arrives: refreshes the designer panels.</summary>
    private void ShowDesigner(ReportDesignModel? model)
    {
        if (model?.SelectedItem is { } name && !SelectedItems.Contains(name))
        {
            SelectedItems.Clear();
            SelectedItems.Add(name);
        }
        else if (model?.SelectedItem is null)
        {
            SelectedItems.Clear();
        }

        RefreshBandTools();
        Replace(PropertySections, model?.Properties);
        var item = SelectedTablix ?? SelectedDesignItem;
        Replace(RowGroups, item?.RowGroups);
        Replace(ColumnGroups, item?.ColumnGroups);
        RefreshGrouping();
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(SelectedDesignItem));
        OnPropertyChanged(nameof(HasGroupTarget));
        OnPropertyChanged(nameof(Arrangements));
        OnPropertyChanged(nameof(LayoutSummary));
        ShowWorkbench(model);
        if (IsXmlView)
        {
            _ = LoadXmlAsync();
        }

        DesignChanged?.Invoke();
    }

    /// <summary>Selects an item (or the report when <paramref name="name"/> is null); with add, extends the selection.</summary>
    public Task SelectAsync(string? name, bool add = false) => WithReportAsync(async report =>
    {
        if (add && name is not null)
        {
            if (!SelectedItems.Remove(name))
            {
                SelectedItems.Add(name);
            }

            OnPropertyChanged(nameof(SelectionReadout));
            OnPropertyChanged(nameof(CanFormat));
            OnPropertyChanged(nameof(Arrangements));
            DesignChanged?.Invoke();
            return;
        }

        SelectedItems.Clear();
        if (name is not null)
        {
            SelectedItems.Add(name);
            IsPropertiesOpen = true;
        }

        Show(await _service.OpenAsync(report.Id, name, CancellationToken.None));
    });

    /// <summary>Places a toolbox item or field at a position (inches) in a region.</summary>
    public Task PlaceAsync(string kind, double leftInches, double topInches, string region) => WithReportAsync(async report =>
    {
        var dataSet = SelectedDataSet?.Name;
        if (kind is "Header" or "Footer")
        {
            // The Bands widgets switch the page header or footer on and off.
            var shown = kind == "Header" ? report.HasHeader : report.HasFooter;
            var band = kind == "Header" ? "page header" : "page footer";
            var items = report.Items.Count(item => item.Region == kind);
            if (shown && items > 0 && System.Windows.MessageBox.Show(
                    _owner(),
                    $"Remove the {band} and the {items} item(s) in it?\n\nUndo (Ctrl+Z) brings them back.",
                    "Remove " + band,
                    System.Windows.MessageBoxButton.OKCancel,
                    System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.OK)
            {
                return;
            }

            Show(await _service.ToggleBandAsync(report.Id, kind, !shown, CancellationToken.None));
            Succeed($"Page {kind.ToLowerInvariant()} {(shown ? "removed" : "added")}.");
            return;
        }

        Show(await _service.PlaceInAsync(report.Id, kind, Math.Max(0, leftInches), topInches, region, dataSet, CancellationToken.None));
        Succeed($"Placed {kind.Replace("Chart:", "", StringComparison.Ordinal)} in {region.ToLowerInvariant()}.");
    });

    public Task MoveAsync(string item, double left, double top, double width, double height) => WithReportAsync(async report =>
        Show(await _service.MoveAsync(report.Id, item, Math.Max(0, left), Math.Max(0, top), Math.Max(0.05, width), Math.Max(0.02, height), CancellationToken.None)));

    public Task ResizeBandAsync(string region, double height) => WithReportAsync(async report =>
        Show(await _service.ResizeBandAsync(report.Id, region, Math.Max(0.1, height), CancellationToken.None)));

    public Task SetPropertyAsync(string key, string? value) => WithReportAsync(async report =>
        Show(await _service.SetPropertyAsync(report.Id, SelectedItem, key, value, CancellationToken.None)));

    [RelayCommand]
    private void PlaceTool(ToolboxEntry? entry)
    {
        if (entry is not null)
        {
            _ = PlaceAsync(entry.Kind, 0.1, -1, "Body");
        }
    }

    [RelayCommand]
    private Task DeleteItemAsync() => WithReportAsync(async report =>
    {
        foreach (var name in SelectedItems.ToArray())
        {
            report = await _service.RemoveItemAsync(report.Id, name, CancellationToken.None);
        }

        SelectedItems.Clear();
        Show(report);
    });

    [RelayCommand]
    private Task CopyItemAsync() => ClipboardAsync(cut: false);

    [RelayCommand]
    private Task CutItemAsync() => ClipboardAsync(cut: true);

    [RelayCommand]
    private Task PasteItemAsync() => WithReportAsync(async report =>
        Show(await _service.PasteAsync(report.Id, SelectedDesignItem?.Region ?? "Body", CancellationToken.None)));

    [RelayCommand]
    private Task ArrangeAsync(string? operation) => WithReportAsync(async report =>
    {
        if (string.IsNullOrEmpty(operation) || SelectedItems.Count == 0)
        {
            Fail("Select one or more items first.");
            return;
        }

        if (!Arrangements[operation])
        {
            // The pane hides these; a keyboard shortcut or stale button must not reach the engine.
            Fail(operation.StartsWith("Distribute", StringComparison.Ordinal)
                ? "Select three or more items to space them evenly."
                : "Select two or more items (Ctrl+click) to make them the same size.");
            return;
        }

        Show(SelectedItems.Count == 1
            ? await _service.ArrangeAsync(report.Id, SelectedItems[0], operation, CancellationToken.None)
            : await _service.ArrangeManyAsync(report.Id, SelectedItems.ToArray(), operation, CancellationToken.None));
    });

    [RelayCommand]
    private void ZoomIn() => Zoom = Math.Min(4, Math.Round(Zoom + 0.1, 2));

    [RelayCommand]
    private void ZoomOut() => Zoom = Math.Max(0.25, Math.Round(Zoom - 0.1, 2));

    [RelayCommand]
    private void ZoomReset() => Zoom = 1;

    [RelayCommand]
    private Task ToggleHeaderAsync() => WithReportAsync(async report =>
        Show(await _service.ToggleBandAsync(report.Id, "Header", !report.HasHeader, CancellationToken.None)));

    [RelayCommand]
    private Task ToggleFooterAsync() => WithReportAsync(async report =>
        Show(await _service.ToggleBandAsync(report.Id, "Footer", !report.HasFooter, CancellationToken.None)));

    [RelayCommand]
    private Task AddGroupAsync(string? axis) => WithReportAsync(async report =>
    {
        var table = SelectedTablix?.Name;
        var field = string.IsNullOrWhiteSpace(GroupField) ? SelectedField?.Name : GroupField;
        if (table is null || string.IsNullOrWhiteSpace(field))
        {
            Fail("Select a table on the surface and a field on the Data tab.");
            return;
        }

        Show(await _service.AddGroupAsync(report.Id, table, axis ?? "Row", field, CancellationToken.None));
        Succeed($"Grouped {table} by {field}.");
    });

    [RelayCommand]
    private Task RemoveGroupAsync(DesignGroup? group) => WithReportAsync(async report =>
    {
        if (group is not null && SelectedTablix?.Name is { } table)
        {
            Show(await _service.RemoveGroupAsync(report.Id, table, group.Name, CancellationToken.None));
        }
    });

    /// <summary>
    /// Preview: asks for the report parameters first, then renders inside REX (embedded viewer with export).
    /// </summary>
    [RelayCommand]
    private Task PreviewAsync() => WithReportAsync(async report =>
    {
        if (await ReportPreviewWindow.ShowAsync(_service, report, Defaults(report), _owner()))
        {
            Succeed("Preview opened.");
        }
    });

    /// <summary>The design service, for hosts that render the preview themselves.</summary>
    public IReportDesignService Service => _service;

    private Task ClipboardAsync(bool cut) => WithReportAsync(async report =>
    {
        if (SelectedItems.Count == 0)
        {
            return;
        }

        Show(await _service.CopyAsync(report.Id, SelectedItems[0], cut, CancellationToken.None));
        Succeed(cut ? "Cut." : "Copied.");
    });

    private static ToolboxEntry T(string kind, string label, string icon) => new(kind, label, icon);
}
