using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReportExpert.App.Assets;
using ReportExpert.App.Reports;
using ReportExpert.App.Theming;
using ReportExpert.App.Shell;
using ReportExpert.App.Workspace;
using ReportExpert.Common;
using ReportExpert.Domain.Models;
using ReportExpert.Editors.Editors;
using ReportExpert.Modules.Preview.Services;
using ReportExpert.Modules.Preview.Views.Dialogs;
using ReportExpert.Reporting.Design;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ReportExpert.App;

public partial class MainWindow : FluentWindow
{
    private readonly ShellViewModel _shellViewModel;
    private readonly WorkspaceService _workspaceService;
    private readonly SettingsService _settingsService;
    private readonly ShellCoordinator _coordinator;
    private readonly ServiceProvider _reportServices;
    private readonly ReportCenterViewModel _reportCenter;

    public MainWindow()
    {
        InitializeComponent();

        _workspaceService = new WorkspaceService();
        _settingsService = new SettingsService();
        EditorServiceBootstrap.Initialize();
        ThemeLogo.Bind(AppLogo);

        _shellViewModel = new ShellViewModel(_workspaceService, () => this);
        DataContext = _shellViewModel;
        HomeFeature.DataContext = _shellViewModel;
        WorkspaceExplorer.DataContext = _shellViewModel;

        _coordinator = new ShellCoordinator(
            HomeFeature.HomeCopilotPanel,
            _workspaceService,
            _shellViewModel.HomeViewer);
        _coordinator.Initialize();

        _shellViewModel.FileOpenRequested += OnWorkspaceFileOpenRequested;
        _shellViewModel.WorkspaceChanged += OnWorkspaceChanged;
        _shellViewModel.HomeViewer.OpenLayoutInPreviewRequested += OnOpenLayoutInPreviewRequested;

        SettingsFeature.Initialize(
            _settingsService,
            HomeFeature.HomeCopilotPanel.ViewModel.CopilotSettingsService,
            onSettingsSaved: () =>
            {
                ApplyThemeFromSettings();
                ApplySampleRows();
                HomeFeature.HomeCopilotPanel.ViewModel.ReloadSettings();
                HomeFeature.ApplyDesignerViewSettings();
            },
            reloadCopilot: () => HomeFeature.HomeCopilotPanel.ViewModel.ReloadSettings(),
            mcpServers: _coordinator.McpServers,
            restartMcpAsync: _coordinator.RestartMcpAsync);

        HomeFeature.AttachSettings(_settingsService);

        // Rex's "Open Settings" (no usable AI provider, or "Manage providers…") opens the providers section.
        HomeFeature.HomeCopilotPanel.ViewModel.OpenSettingsRequested = () =>
        {
            NavSettings.IsChecked = true;
            Dispatcher.BeginInvoke(SettingsFeature.ShowProviders, DispatcherPriority.Loaded);
        };

        // REX V3: the SafiPay report design engine, behind the report center.
        _reportServices = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddReportDesign(options => options.SampleRows = SampleRowsSetting())
            .BuildServiceProvider();
        _reportCenter = new ReportCenterViewModel(_reportServices.GetRequiredService<IReportDesignService>(), () => this);
        HomeFeature.AttachReportDesigner(_reportCenter);
        DesignerStatus.DataContext = _reportCenter;
        WorkspaceExplorer.CollapseRequested += () => SetDrawerCollapsed(true);

        // The MCP servers are child processes. Closing the window has to take them with it.
        Closed += async (_, _) =>
        {
            await _coordinator.DisposeAsync();
            await _reportServices.DisposeAsync();
        };
    }

    /// <summary>Settings › Preview › Sample rows, as the report engine's sample-data row count.</summary>
    private int SampleRowsSetting() =>
        Math.Clamp(_settingsService.Settings.RowsGenerated, 1, ReportingOptions.MaxSampleRows);

    /// <summary>Applies a changed sample row count to the designer preview (the next render uses it).</summary>
    private void ApplySampleRows()
    {
        if (_reportServices?.GetService<Microsoft.Extensions.Options.IOptions<ReportingOptions>>() is { } options)
            options.Value.SampleRows = SampleRowsSetting();
    }

    private void OnWorkspaceChanged(WorkspaceSnapshot? snapshot)
    {
        NavSettings.IsChecked = false;
        _reportCenter.SetWorkspaceLayouts(snapshot?.Layouts.Select(layout => layout.FullPath) ?? []);
        if (snapshot is null)
            HomeFeature.HomeCopilotPanel.ViewModel.ClearReportContext();
    }

    private async void OnWorkspaceFileOpenRequested(WorkspaceFileEntry entry)
    {
        try
        {
            NavSettings.IsChecked = false;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await _shellViewModel.HomeViewer.OpenAsync(entry);
            _coordinator.NotifyReportOpened(entry.FullPath);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Failed to open '{entry.Name}':\n\n{ex.Message}",
                AppConstants.ApplicationName,
                System.Windows.MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnOpenLayoutInPreviewRequested(string layoutPath)
    {
        try
        {
            _coordinator.NotifyReportOpened(layoutPath);
            RdlcPreviewWindow.ShowDialog(layoutPath, owner: this, settings: _settingsService);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Failed to open preview for '{Path.GetFileName(layoutPath)}':\n\n{ex.Message}",
                AppConstants.ApplicationName,
                System.Windows.MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnCollapseDrawer(object sender, RoutedEventArgs e) => SetDrawerCollapsed(true);

    private void OnExpandDrawer(object sender, RoutedEventArgs e) => SetDrawerCollapsed(false);

    /// <summary>Collapses the workspace panel to a thin vertical "Reports" tab, or opens it again.</summary>
    private void SetDrawerCollapsed(bool collapsed)
    {
        DrawerColumn.Width = new GridLength(collapsed ? 30 : 250);
        DrawerPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        ReportsTab.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyThemeFromSettings()
    {
        SafiThemeManager.ApplyTheme(_settingsService.Settings.Theme);

        ThemeLogo.Update(AppLogo);
        EditorServicesLocator.ThemeService?.NotifyThemeChanged();
    }
}
