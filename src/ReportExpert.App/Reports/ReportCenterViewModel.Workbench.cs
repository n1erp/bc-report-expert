using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReportExpert.Reporting.Design;

namespace ReportExpert.App.Reports;

/// <summary>A node of the Report Data pane: a folder, or something that can be placed on the page.</summary>
/// <param name="Label">What the tree shows.</param>
/// <param name="Kind">Folder, Field, Parameter or Global — picks the icon.</param>
/// <param name="DropKind">What a drag or double-click places (<c>Field:Set/Name</c> or <c>Expr:=…</c>); empty for a folder.</param>
/// <param name="Expression">The expression it stands for, shown as the tooltip and inserted by the expression editor.</param>
public sealed record ReportDataNode(string Label, string Kind, string DropKind, string Expression, IReadOnlyList<ReportDataNode> Children)
    : INotifyPropertyChanged
{
    private bool _isExpanded;

    public bool IsFolder => Kind == "Folder";

    /// <summary>Identifies a folder across layouts and restarts, so its open/closed state can be remembered.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Whether the folder is open. Folders start collapsed; what the user opens is remembered.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            if (IsFolder)
                DesignerPanelState.SetFolderExpanded(Key, value);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>A folder node, opened if the user left it open last time.</summary>
    public static ReportDataNode Folder(string key, string label, IReadOnlyList<ReportDataNode> children) =>
        new(label, "Folder", "", "", children) { Key = key, IsExpanded = DesignerPanelState.IsFolderExpanded(key) };
}

/// <summary>
/// The designer workbench, modelled on Report Builder's design view: the Report Data pane, the format toolbar,
/// keyboard nudging, page setup, expression placeholders and the selection readout. Every edit is one
/// revision, whatever the size of the selection, so one undo reverts it.
/// </summary>
public sealed partial class ReportCenterViewModel
{
    /// <summary>Report Builder's built-in fields (Globals and User), in its order.</summary>
    public static IReadOnlyList<(string Name, string Expression)> BuiltInFields { get; } =
    [
        ("ExecutionTime", "=Globals!ExecutionTime"),
        ("OverallPageNumber", "=Globals!OverallPageNumber"),
        ("OverallTotalPages", "=Globals!OverallTotalPages"),
        ("PageName", "=Globals!PageName"),
        ("PageNumber", "=Globals!PageNumber"),
        ("RenderFormat", "=Globals!RenderFormat.Name"),
        ("ReportFolder", "=Globals!ReportFolder"),
        ("ReportName", "=Globals!ReportName"),
        ("ReportServerUrl", "=Globals!ReportServerUrl"),
        ("TotalPages", "=Globals!TotalPages"),
        ("UserID", "=User!UserID"),
        ("Language", "=User!Language"),
    ];

    public IReadOnlyList<string> FontFamilies { get; } =
        ["Segoe UI", "Arial", "Calibri", "Tahoma", "Verdana", "Times New Roman", "Georgia", "Courier New", "Consolas", "Arial Narrow"];

    public IReadOnlyList<string> FontSizes { get; } =
        ["6pt", "7pt", "8pt", "9pt", "10pt", "11pt", "12pt", "14pt", "16pt", "18pt", "20pt", "24pt", "28pt", "36pt"];

    /// <summary>Number formats offered by the format toolbar, as (label, RDL format string).</summary>
    public static IReadOnlyList<(string Label, string Format)> NumberFormats { get; } =
    [
        ("General", ""),
        ("Number  1,234.57", "#,##0.00"),
        ("Integer  1,235", "#,##0"),
        ("Currency  €1,234.57", "C2"),
        ("Percent  12.35%", "P2"),
        ("Date  31/12/2026", "d"),
        ("Long date", "D"),
        ("Date & time", "g"),
        ("Hide zero  1,234.57 / blank", "#,##0.00;-#,##0.00;\"\""),
    ];

    /// <summary>The Report Data pane: built-in fields, parameters, then each dataset and its fields.</summary>
    public ObservableCollection<ReportDataNode> ReportData { get; } = [];

    /// <summary>The property sections after the search filter and the categorized/alphabetical choice.</summary>
    public ObservableCollection<DesignPropertySection> DisplaySections { get; } = [];

    /// <summary>Simple expressions as [Field] / [@Param] / [&amp;Global] and complex ones as «Expr», as in Report Builder.</summary>
    [ObservableProperty]
    private bool _showPlaceholders = true;

    /// <summary>Items hidden at run time are drawn dimmed; off removes them so what is under them can be reached.</summary>
    [ObservableProperty]
    private bool _showHiddenItems = true;

    [ObservableProperty]
    private string _propertySearch = string.Empty;

    [ObservableProperty]
    private bool _isAlphabetical;

    /// <summary>Opened by the view with the page setup dialog; the view model only applies the result.</summary>
    public Func<DesignPageSetup, double, DesignPageSetup?>? PageSetupRequested { get; set; }

    /// <summary>Opened by the view with the expression editor: (title, current value, context) → new value, or null when cancelled.</summary>
    public Func<string, string, ExpressionContext, string?>? ExpressionEditorRequested { get; set; }

    public DesignPageSetup? Page => Current?.Page;

    /// <summary>The blank-page warning (body wider than the printable width), or null.</summary>
    public string? PageWarning => Current?.Warnings.FirstOrDefault(warning => warning.Contains("blank pages", StringComparison.Ordinal));

    public bool HasPageWarning => PageWarning is not null;

    /// <summary>"Textbox12 · X 1.2cm  Y 0.4cm · W 3cm  H 0.5cm", or the page size when nothing is selected.</summary>
    public string SelectionReadout
    {
        get
        {
            var metric = Page?.IsMetric ?? true;
            string L(double inches) => ReportPageSetup.Format(inches, metric);
            if (SelectedDesignItem is { } item)
            {
                var more = SelectedItems.Count > 1 ? $"  (+{SelectedItems.Count - 1})" : "";
                return $"{item.Name}{more} · X {L(item.LeftInches)}  Y {L(item.TopInches)} · W {L(item.WidthInches)}  H {L(item.HeightInches)}";
            }

            return Page is { } page
                ? $"{page.PaperSize} {page.Orientation.ToLowerInvariant()} · {L(page.PageWidthInches)} × {L(page.PageHeightInches)} · body {L(Current?.BodyWidthInches ?? 0)} of {L(page.MaxBodyWidthInches)}"
                : string.Empty;
        }
    }

    /// <summary>True when the selection holds text the format toolbar can style.</summary>
    public bool CanFormat => FormatTargets().Count > 0;

    public string FormatFontFamily => PropertyValue("FontFamily");

    public string FormatFontSize => PropertyValue("FontSize");

    public bool IsBold => PropertyValue("FontWeight") is "Bold" or "SemiBold" or "ExtraBold" or "Heavy" or "Black";

    public bool IsItalic => PropertyValue("FontStyle") == "Italic";

    public bool IsUnderline => PropertyValue("TextDecoration") == "Underline";

    public string FormatTextAlign => PropertyValue("TextAlign");

    public string FormatVerticalAlign => PropertyValue("VerticalAlign");

    partial void OnShowPlaceholdersChanged(bool value) => DesignChanged?.Invoke();

    partial void OnShowHiddenItemsChanged(bool value) => DesignChanged?.Invoke();

    partial void OnPropertySearchChanged(string value) => RefreshDisplaySections();

    partial void OnIsAlphabeticalChanged(bool value) => RefreshDisplaySections();

    /// <summary>Refreshes everything the workbench derives from the current model.</summary>
    private void ShowWorkbench(ReportDesignModel? model)
    {
        RefreshDisplaySections();
        RefreshReportData(model);
        foreach (var name in new[]
                 {
                     nameof(Page), nameof(PageWarning), nameof(HasPageWarning), nameof(SelectionReadout), nameof(CanFormat),
                     nameof(FormatFontFamily), nameof(FormatFontSize), nameof(IsBold), nameof(IsItalic), nameof(IsUnderline),
                     nameof(FormatTextAlign), nameof(FormatVerticalAlign),
                 })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>
    /// Properties the format toolbar above the surface already edits (font, size, bold, italic, underline,
    /// colour, alignment, number format, and the value through Expression). The Properties pane leaves them
    /// out while the toolbar applies to the selection, so each property has one place.
    /// </summary>
    private static readonly HashSet<string> ToolbarProperties = new(StringComparer.Ordinal)
    {
        "Value", "FontFamily", "FontSize", "FontWeight", "FontStyle", "TextDecoration", "Color", "Format", "TextAlign", "VerticalAlign",
    };

    private void RefreshDisplaySections()
    {
        var query = PropertySearch.Trim();
        IEnumerable<DesignPropertySection> sections = PropertySections;
        if (CanFormat)
        {
            sections = sections
                .Select(section => section with { Properties = section.Properties.Where(property => !ToolbarProperties.Contains(property.Key)).ToArray() })
                .Where(section => section.Properties.Count > 0)
                .ToArray();
        }

        if (IsAlphabetical)
        {
            sections = [new DesignPropertySection("All properties", sections.SelectMany(section => section.Properties)
                .GroupBy(property => property.Key, StringComparer.Ordinal).Select(group => group.First())
                .OrderBy(property => property.Key, StringComparer.OrdinalIgnoreCase).ToArray())];
        }

        if (query.Length > 0)
        {
            sections = sections
                .Select(section => section with
                {
                    Properties = section.Properties.Where(property =>
                        property.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || section.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || property.Value.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray(),
                })
                .Where(section => section.Properties.Count > 0);
        }

        Replace(DisplaySections, sections.ToArray());
    }

    private void RefreshReportData(ReportDesignModel? model)
    {
        var nodes = new List<ReportDataNode>
        {
            ReportDataNode.Folder("builtin", "Built-in Fields", BuiltInFields.Select(field => new ReportDataNode(field.Name, "Global", "Expr:" + field.Expression, field.Expression, [])).ToArray()),
            ReportDataNode.Folder("parameters", "Parameters", (model?.Parameters ?? []).Select(parameter =>
                new ReportDataNode(parameter.Name, "Parameter", "Expr:=Parameters!" + parameter.Name + ".Value", "=Parameters!" + parameter.Name + ".Value", [])).ToArray()),
        };

        foreach (var dataSet in model?.DataSets ?? [])
        {
            nodes.Add(ReportDataNode.Folder("dataset:" + dataSet.Name, dataSet.Name, dataSet.Fields.Select(field =>
                new ReportDataNode(field.Name, "Field", $"Field:{dataSet.Name}/{field.Name}", "=Fields!" + field.Name + ".Value", [])).ToArray()));
        }

        // Rebuild only when something changed, so the tree keeps its expansion state while editing.
        if (ReportData.Count == nodes.Count && ReportData.Zip(nodes).All(pair => pair.First.Label == pair.Second.Label && pair.First.Children.Count == pair.Second.Children.Count))
        {
            return;
        }

        Replace(ReportData, nodes);
    }

    /// <summary>Double-click in the Report Data pane: a text box with that value, under what is in the body.</summary>
    public Task InsertDataNodeAsync(ReportDataNode node) =>
        node.IsFolder || node.DropKind.Length == 0 ? Task.CompletedTask : PlaceAsync(node.DropKind, 0.1, -1, "Body");

    private string PropertyValue(string key) =>
        PropertySections.SelectMany(section => section.Properties).FirstOrDefault(property => property.Key == key)?.Value ?? string.Empty;

    /// <summary>The selected items the format toolbar applies to: text boxes and table cells.</summary>
    private IReadOnlyList<string> FormatTargets()
    {
        if (Current is not { } report)
        {
            return [];
        }

        if (SelectedItems.Count == 0)
        {
            // A table cell is selected by name but is not a top-level item.
            return Current.SelectedKind is "Textbox" or "Cell" && Current.SelectedItem is { } cell ? [cell] : [];
        }

        return SelectedItems.Where(name =>
            report.Items.FirstOrDefault(item => item.Name == name) is { Kind: "Textbox" }
            || (name == report.SelectedItem && report.SelectedKind is "Textbox" or "Cell")).ToArray();
    }

    /// <summary>Applies one format property to every selected text box, as one revision.</summary>
    [RelayCommand]
    private Task SetFormatAsync(string? keyValue) => WithReportAsync(async report =>
    {
        if (keyValue?.Split('|', 2) is not [var key, var value])
        {
            return;
        }

        await ApplyFormatAsync(report, key, value.Length == 0 ? null : value);
    });

    [RelayCommand]
    private Task ToggleBoldAsync() => WithReportAsync(report => ApplyFormatAsync(report, "FontWeight", IsBold ? "Normal" : "Bold"));

    [RelayCommand]
    private Task ToggleItalicAsync() => WithReportAsync(report => ApplyFormatAsync(report, "FontStyle", IsItalic ? "Normal" : "Italic"));

    [RelayCommand]
    private Task ToggleUnderlineAsync() => WithReportAsync(report => ApplyFormatAsync(report, "TextDecoration", IsUnderline ? "None" : "Underline"));

    private async Task ApplyFormatAsync(ReportDesignModel report, string key, string? value)
    {
        var targets = FormatTargets();
        if (targets.Count == 0)
        {
            Fail("Select one or more text boxes first.");
            return;
        }

        var changes = targets.Select(target => new ReportPropertyChange(target, key, value)).ToArray();
        Show(await _service.SetPropertiesAsync(report.Id, changes, report.SelectedItem, CancellationToken.None));
    }

    /// <summary>
    /// Arrow keys: moves the selection by one snap step (or the grid, with Ctrl); with Shift, resizes it. All
    /// selected items move together in one revision.
    /// </summary>
    public Task NudgeAsync(int dx, int dy, bool resize, bool large) => WithReportAsync(async report =>
    {
        var items = report.Items.Where(item => SelectedItems.Contains(item.Name)).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        var step = large ? 0.125 : 1.0 / 32;
        var changes = new List<ReportPropertyChange>();
        foreach (var item in items)
        {
            if (resize)
            {
                changes.Add(new(item.Name, "Width", Inches(Math.Max(0.05, item.WidthInches + dx * step))));
                changes.Add(new(item.Name, "Height", Inches(Math.Max(0.02, item.HeightInches + dy * step))));
            }
            else
            {
                changes.Add(new(item.Name, "Left", Inches(Math.Max(0, item.LeftInches + dx * step))));
                changes.Add(new(item.Name, "Top", Inches(Math.Max(0, item.TopInches + dy * step))));
            }
        }

        Show(await _service.SetPropertiesAsync(report.Id, changes, report.SelectedItem, CancellationToken.None));
    });

    private static string Inches(double value) => value.ToString("0.####", CultureInfo.InvariantCulture) + "in";

    /// <summary>Opens the page setup dialog and applies the result as one revision.</summary>
    [RelayCommand]
    private Task PageSetupAsync() => WithReportAsync(async report =>
    {
        if (report.Page is not { } page || PageSetupRequested?.Invoke(page, report.BodyWidthInches) is not { } edited)
        {
            return;
        }

        string L(double inches) => ReportPageSetup.Format(inches, edited.IsMetric);
        const string Target = ReportPageSetup.ReportTarget;
        var changes = new List<ReportPropertyChange>
        {
            new(Target, "PageWidth", L(edited.PageWidthInches)),
            new(Target, "PageHeight", L(edited.PageHeightInches)),
            new(Target, "LeftMargin", L(edited.LeftMarginInches)),
            new(Target, "RightMargin", L(edited.RightMarginInches)),
            new(Target, "TopMargin", L(edited.TopMarginInches)),
            new(Target, "BottomMargin", L(edited.BottomMarginInches)),
            new(Target, "Columns", edited.Columns.ToString(CultureInfo.InvariantCulture)),
            new(Target, "ColumnSpacing", L(edited.ColumnSpacingInches)),
        };

        var updated = await _service.SetPropertiesAsync(report.Id, changes, report.SelectedItem, CancellationToken.None);
        Show(updated);
        Succeed(updated.Page is { } now && ReportPageSetup.BlankPageWarning(now, updated.BodyWidthInches) is not null
            ? "Page setup saved. The body is still wider than the page — use Fit body to page."
            : "Page setup saved.");
    });

    [RelayCommand]
    private Task FitBodyToPageAsync() => WithReportAsync(async report =>
    {
        var updated = await _service.FitBodyToPageAsync(report.Id, CancellationToken.None);
        Show(updated);
        Succeed(updated.Page is { } page && ReportPageSetup.BlankPageWarning(page, updated.BodyWidthInches) is null
            ? "The body now fits between the margins."
            : "The body is as narrow as its items allow; move or narrow the right-most items, then try again.");
    });

    /// <summary>
    /// Opens the expression editor for a property of the selection (Value when the key is empty) and saves the
    /// result. The editor lists the fields of the selection's dataset, the parameters and the built-in fields.
    /// </summary>
    public Task EditExpressionAsync(string? key) => WithReportAsync(async report =>
    {
        key = string.IsNullOrEmpty(key) ? "Value" : key;
        var current = PropertyValue(key);
        var dataSet = SelectedDesignItem?.DataSetName;
        var context = new ExpressionContext(
            report.DataSets.Where(set => dataSet is null || set.Name == dataSet).SelectMany(set => set.Fields.Select(field => (set.Name, field.Name))).ToArray(),
            report.Parameters.Select(parameter => parameter.Name).ToArray(),
            report.HasCustomCode);
        if (ExpressionEditorRequested?.Invoke($"{SelectedItem} · {key}", current, context) is not { } edited || edited == current)
        {
            return;
        }

        Show(await _service.SetPropertyAsync(report.Id, SelectedItem, key, edited, CancellationToken.None));
        Succeed($"{key} of {SelectedItem} updated.");
    });

    [RelayCommand]
    private Task EditValueAsync() => EditExpressionAsync("Value");

    /// <summary>Zooms so the page width fills the surface.</summary>
    public void ZoomToWidth(double availablePixels)
    {
        var width = Math.Max(Current?.BodyWidthInches ?? 0, Page?.MaxBodyWidthInches ?? 0);
        if (width <= 0 || availablePixels <= 100)
        {
            return;
        }

        Zoom = Math.Clamp(Math.Round((availablePixels - 80) / (width * 96), 2), 0.25, 4);
    }
}

/// <summary>What the expression editor can offer: fields (dataset, field), parameters and whether the report has custom code.</summary>
public sealed record ExpressionContext(IReadOnlyList<(string DataSet, string Field)> Fields, IReadOnlyList<string> Parameters, bool HasCustomCode);
