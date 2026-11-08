using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ReportExpert.App.Assets;
using ReportExpert.App.Helpers;
using ReportExpert.App.Shell;
using ReportExpert.Domain.Models;
using ReportExpert.Modules.Copilot.Views;
using ReportExpert.Modules.Preview.Services;
using ReportExpert.App.Reports;

namespace ReportExpert.App.Home;

public partial class HomeView : System.Windows.Controls.UserControl
{
    private readonly HomeOfficeDocumentController _office;
    private readonly ReportCenterView _studio = new() { IsEmbedded = true };
    private ReportCenterViewModel? _studioModel;
    private int _loadedRevision;
    private SideTool _active = SideTool.None;
    private bool _syncing;

    private enum SideTool { None, Properties, Rex, Outline }
    private bool _studioDirty;
    private HomeWorkspaceViewModel? _viewer;
    private SettingsService? _settings;

    /// <summary>Home-embedded Copilot chat (shared with shell wiring).</summary>
    public CopilotView HomeCopilotPanel => HomeCopilot;

    public HomeView()
    {
        Resources.Add("RecentVisConverter", new CollectionCountToVisibilityConverter());
        InitializeComponent();
        _office = new HomeOfficeDocumentController(ExcelGrid, ExcelFallbackGrid, WordPreview);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    /// <summary>Connects app settings so designer rulers/gridlines stay in sync.</summary>
    public void AttachSettings(SettingsService settings)
    {
        _settings = settings;
        ApplyDesignerViewSettings();
    }

    /// <summary>Re-reads designer chrome preferences after settings save.</summary>
    public void ApplyDesignerViewSettings()
    {
        if (_settings is null)
            return;

        if (_studioModel is not null)
            _studioModel.ShowGrid = _settings.Settings.ShowDesignerGridlines;
    }

    /// <summary>
    /// Connects the V3 report designer (design surface, data, parameters, history, export), which replaces
    /// the old minimal layout editor for RDLC layouts.
    /// </summary>
    public void AttachReportDesigner(ReportCenterViewModel model)
    {
        _studioModel = model;
        _studio.DataContext = model;
        SideProperties.DataContext = model;
        SideOutline.DataContext = model;
        model.PropertyChanged += (_, e) =>
        {
            // Selecting an item asks for properties: show them unless Rex is in use.
            if (e.PropertyName == nameof(ReportCenterViewModel.IsPropertiesOpen) && !_syncing
                && model.IsPropertiesOpen && _active is SideTool.None)
            {
                Activate(SideTool.Properties);
            }

            if (e.PropertyName == nameof(ReportCenterViewModel.IsDesignTabActive))
                UpdateSideBar();

            if (e.PropertyName == nameof(ReportCenterViewModel.Current)
                && model.Current is { } current
                && current.Revision != _loadedRevision
                && _viewer?.IsRdlcLayout == true)
            {
                _studioDirty = true;
                _viewer.MarkDirty();
            }
        };
        ApplyDesignerViewSettings();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeLogo.Bind(HomeLogo);
        WireViewer();
        RequestDeferredEditorLoad();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => UnwireViewer();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => WireViewer();

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            RequestDeferredEditorLoad();
    }

    private void WireViewer()
    {
        UnwireViewer();
        if (DataContext is ShellViewModel shell)
        {
            _viewer = shell.HomeViewer;
            _viewer.LayoutEditorLoadRequested += OnLayoutEditorLoadRequested;
            _viewer.LayoutEditorSaveRequested += OnLayoutEditorSaveRequested;
            _viewer.PropertyChanged += OnViewerPropertyChanged;
            _office.SetDirtyHandler(() => _viewer?.MarkDirty());
        }

        UpdateSideBar();
    }

    private void UnwireViewer()
    {
        if (_viewer is null)
            return;

        _viewer.LayoutEditorLoadRequested -= OnLayoutEditorLoadRequested;
        _viewer.LayoutEditorSaveRequested -= OnLayoutEditorSaveRequested;
        _viewer.PropertyChanged -= OnViewerPropertyChanged;
        _office.SetDirtyHandler(null);
        _viewer = null;
    }

    private void OnViewerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomeWorkspaceViewModel.IsCopilotOpen) && !_syncing && _viewer is not null)
            Activate(_viewer.IsCopilotOpen ? SideTool.Rex : (_active == SideTool.Rex ? SideTool.None : _active));
        if (e.PropertyName == nameof(HomeWorkspaceViewModel.IsRdlcLayout) && _viewer?.IsRdlcLayout != true)
            Activate(SideTool.None);
        if (e.PropertyName == nameof(HomeWorkspaceViewModel.IsRdlcLayout))
            UpdateSideBar();

        if (e.PropertyName is nameof(HomeWorkspaceViewModel.IsExcelLayout)
            or nameof(HomeWorkspaceViewModel.IsWordLayout)
            or nameof(HomeWorkspaceViewModel.IsRdlcLayout)
            or nameof(HomeWorkspaceViewModel.FilePath))
        {
            RequestDeferredEditorLoad();
        }
    }

    /// <summary>Activity bar click: open that tool in the side panel, or close it when it is already open.</summary>
    private void OnActivity(object sender, RoutedEventArgs e)
    {
        var tool = (sender as FrameworkElement)?.Tag switch
        {
            "Properties" => SideTool.Properties,
            "Rex" => SideTool.Rex,
            "Outline" => SideTool.Outline,
            _ => SideTool.None,
        };

        if (tool == _active)
        {
            Activate(SideTool.None);
        }
        else
        {
            Activate(tool);
        }
    }

    private void OnCloseSidePanel(object sender, RoutedEventArgs e) => Activate(SideTool.None);

    /// <summary>F4 toggles Properties and F5 opens Preview, as in Visual Studio.</summary>
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_viewer?.IsRdlcLayout != true || e.Handled || !IsSideBarAvailable)
            return;

        if (e.Key == Key.F4)
        {
            Activate(_active == SideTool.Properties ? SideTool.None : SideTool.Properties);
            e.Handled = true;
        }
        else if ((e.Key == Key.System ? e.SystemKey : e.Key) == Key.T && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt))
        {
            // Visual Studio's Document Outline shortcut.
            Activate(_active == SideTool.Outline ? SideTool.None : SideTool.Outline);
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            _studioModel?.PreviewCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Shows one tool in the shared side panel (or none) and keeps the view models in step.</summary>
    private void Activate(SideTool tool)
    {
        _active = tool;
        _syncing = true;
        try
        {
            SideProperties.Visibility = tool == SideTool.Properties ? Visibility.Visible : Visibility.Collapsed;
            HomeCopilot.Visibility = tool == SideTool.Rex ? Visibility.Visible : Visibility.Collapsed;
            SideOutline.Visibility = tool == SideTool.Outline ? Visibility.Visible : Visibility.Collapsed;
            SidePanel.Visibility = tool == SideTool.None || !IsSideBarAvailable ? Visibility.Collapsed : Visibility.Visible;
            SidePanel.Width = tool == SideTool.Rex ? 380 : 300;
            SidePanelTitle.Text = tool switch
            {
                SideTool.Properties => "PROPERTIES",
                SideTool.Rex => "REX · AI COPILOT",
                SideTool.Outline => "REPORT ITEMS",
                _ => string.Empty,
            };

            if (_studioModel is not null)
                _studioModel.IsPropertiesOpen = tool == SideTool.Properties;
            if (_viewer is not null && _viewer.IsCopilotOpen != (tool == SideTool.Rex))
                _viewer.IsCopilotOpen = tool == SideTool.Rex;
        }
        finally
        {
            _syncing = false;
        }

        SyncButtons();
    }

    /// <summary>The side bar belongs to the designer: an RDLC layout with its Design tab showing.</summary>
    private bool IsSideBarAvailable => _viewer?.IsRdlcLayout == true && _studioModel?.IsDesignTabActive != false;

    /// <summary>
    /// Hides the side bar and its panel on the Data, Parameters and Layout tabs, and brings them back on Design.
    /// The open tool is kept, so returning to Design shows it again.
    /// </summary>
    private void UpdateSideBar()
    {
        bool available = IsSideBarAvailable;
        ActivityBar.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        SidePanel.Visibility = available && _active != SideTool.None ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SyncButtons()
    {
        ActProperties.IsChecked = _active == SideTool.Properties;
        ActRex.IsChecked = _active == SideTool.Rex;
        ActOutline.IsChecked = _active == SideTool.Outline;
    }

    private void OnLayoutEditorLoadRequested() => RequestDeferredEditorLoad();


    private void RequestDeferredEditorLoad()
    {
        if (_viewer is null || !IsVisible)
            return;

        if (_viewer.IsExcelLayout || _viewer.IsWordLayout)
            Dispatcher.BeginInvoke(LoadOfficeDocumentNow, DispatcherPriority.Loaded);
        else if (_viewer.IsRdlcLayout)
            Dispatcher.BeginInvoke(LoadRdlcVisualNow, DispatcherPriority.Loaded);
        else
            ClearRdlcVisual();
    }

    private void LoadOfficeDocumentNow()
    {
        var viewer = _viewer;
        if (viewer is null)
            return;

        if (string.IsNullOrWhiteSpace(viewer.FilePath) || !File.Exists(viewer.FilePath))
            return;

        ClearRdlcVisual();

        try
        {
            _office.RunWithDirtySuppressed(() =>
            {
                if (viewer.IsExcelLayout)
                    _office.LoadExcel(viewer.FilePath, viewer.MarkLoaded);
                else if (viewer.IsWordLayout)
                    _office.LoadWord(viewer.FilePath, viewer.MarkLoaded);
            });
        }
        catch (Exception ex)
        {
            viewer.ReportEditorError($"Failed to open layout editor: {ex.Message}");
        }
    }

    private async void LoadRdlcVisualNow()
    {
        var viewer = _viewer;
        if (viewer is null || !viewer.IsRdlcLayout || _studioModel is null)
            return;

        if (string.IsNullOrWhiteSpace(viewer.FilePath) || !File.Exists(viewer.FilePath))
            return;

        try
        {
            RdlcVisualHost.ClearValue(WidthProperty);
            RdlcVisualHost.ClearValue(HeightProperty);
            RdlcVisualHost.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            RdlcVisualHost.VerticalAlignment = VerticalAlignment.Stretch;
            RdlcVisualHost.Content = _studio;
            _studioModel.AttachHost(
                Path.GetFileName(viewer.FilePath),
                viewer.SaveCommand,
                viewer.ReloadCommand,
                viewer.ToggleCopilotCommand);
            _loadedRevision = -1;
            await _studioModel.OpenPathAsync(viewer.FilePath);
            _loadedRevision = _studioModel.Current?.Revision ?? 0;
            _studioDirty = false;
            ApplyDesignerViewSettings();
            if (_active == SideTool.None)
                Activate(SideTool.Properties);
            viewer.ClearStatus();
            viewer.MarkLoaded($"{Path.GetFileName(viewer.FilePath)} · Report designer");
        }
        catch (Exception ex)
        {
            RdlcVisualHost.Content = new Border
            {
                Padding = new Thickness(24),
                Child = new TextBlock
                {
                    Text = $"Could not open the report designer:\n{ex.Message}",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = System.Windows.Media.Brushes.DarkRed
                }
            };
            viewer.ReportEditorError($"Failed to open RDLC layout: {ex.Message}");
        }
    }

    private void RdlcLayoutScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Designer view fills the host; page scrolling is handled inside the module.
    }

    private void ClearRdlcVisual()
    {
        if (RdlcVisualHost is not null)
        {
            RdlcVisualHost.Content = null;
            RdlcVisualHost.ClearValue(WidthProperty);
            RdlcVisualHost.ClearValue(HeightProperty);
        }
    }

    private void OnLayoutEditorSaveRequested()
    {
        if (_viewer is null || string.IsNullOrWhiteSpace(_viewer.FilePath))
            return;

        try
        {
            if (_viewer.IsRdlcLayout)
            {
                SaveRdlcAsync(_viewer);
                return;
            }

            if (_viewer.IsExcelLayout)
            {
                if (!_office.TrySaveExcel(_viewer.FilePath, out string? error))
                {
                    _viewer.ReportEditorError(error!);
                    return;
                }

                _viewer.MarkSaved();
                return;
            }

            if (_viewer.IsWordLayout)
                _viewer.ReportEditorError(HomeOfficeDocumentController.WordReadOnlyMessage);
        }
        catch (Exception ex)
        {
            _viewer.ReportEditorError($"Save failed: {ex.Message}");
            System.Windows.MessageBox.Show(
                $"Could not save '{Path.GetFileName(_viewer.FilePath)}':\n\n{ex.Message}",
                "Report Expert",
                System.Windows.MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void SaveRdlcAsync(HomeWorkspaceViewModel viewer)
    {
        if (_studioModel is null)
            return;

        await _studioModel.SaveToFileCommand.ExecuteAsync(null);
        if (_studioModel.IsError)
        {
            viewer.ReportEditorError(_studioModel.Status);
            return;
        }

        _loadedRevision = _studioModel.Current?.Revision ?? _loadedRevision;
        _studioDirty = false;
        viewer.MarkSaved();
    }

    private void OpenWordExternally_Click(object sender, RoutedEventArgs e)
    {
        if (_viewer is null || string.IsNullOrWhiteSpace(_viewer.FilePath) || !File.Exists(_viewer.FilePath))
            return;

        if (!HomeOfficeDocumentController.TryOpenExternally(_viewer.FilePath, out string? error))
        {
            System.Windows.MessageBox.Show(
                $"Could not open Word:\n\n{error}",
                "Report Expert",
                System.Windows.MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RecentProject_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecentProjectEntry entry } &&
            DataContext is ShellViewModel vm)
        {
            vm.OpenRecentProjectCommand.Execute(entry);
        }
    }
}
