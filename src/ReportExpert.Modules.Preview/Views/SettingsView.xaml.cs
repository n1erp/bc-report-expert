using System.Windows;
using System.Windows.Controls;
using ReportExpert.Mcp.Client;
using ReportExpert.Modules.Copilot.Services;
using ReportExpert.Modules.Preview.Services;
using ReportExpert.Modules.Preview.ViewModels;

namespace ReportExpert.Modules.Preview.Views;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    private SettingsViewModel? _viewModel;

    public SettingsView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible || _viewModel is null)
                return;

            _viewModel.ReloadCopilotFromService();
        };
    }

    public void Initialize(
        SettingsService settingsService,
        CopilotSettingsService copilotSettingsService,
        Action? onSettingsSaved = null,
        Action? reloadCopilot = null,
        IMcpServerHost? mcpServers = null,
        Func<Task>? restartMcpAsync = null,
        Action? onToolEnablementChanged = null)
    {
        _viewModel = new SettingsViewModel(settingsService, copilotSettingsService);
        _viewModel.SettingsSaved += () =>
        {
            onSettingsSaved?.Invoke();
            reloadCopilot?.Invoke();
        };
        DataContext = _viewModel;
        _viewModel.AttachMcp(mcpServers, restartMcpAsync, onToolEnablementChanged);
    }

    /// <summary>Each provider row's key box starts with that provider's saved key.</summary>
    private void ProviderKeyBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox { DataContext: ProviderSettingsRowViewModel row } box && box.Password != row.ApiKey)
            box.Password = row.ApiKey;
    }

    private void ProviderKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox { DataContext: ProviderSettingsRowViewModel row } box)
            row.ApiKey = box.Password;
    }

    private void OnOpenRepository(object sender, RoutedEventArgs e) => Open(ReportExpert.Common.ProjectLinks.RepositoryUrl);

    private void OnOpenIssues(object sender, RoutedEventArgs e) => Open(ReportExpert.Common.ProjectLinks.IssuesUrl);

    private void OnReportBug(object sender, RoutedEventArgs e) =>
        Open(ReportExpert.Common.ProjectLinks.NewIssue(ReportExpert.Common.ProjectLinks.BugTemplate, "[Bug] ", new Dictionary<string, string>
        {
            ["version"] = ReportExpert.Common.ErrorReport.VersionText(),
            ["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        }));

    private void OnRequestFeature(object sender, RoutedEventArgs e) =>
        Open(ReportExpert.Common.ProjectLinks.NewIssue(ReportExpert.Common.ProjectLinks.FeatureTemplate, "[Feature] "));

    private void Open(string url)
    {
        if (!ReportExpert.Common.ProjectLinks.Open(url) && _viewModel is not null)
        {
            System.Windows.Clipboard.SetText(url);
            System.Windows.MessageBox.Show(Window.GetWindow(this), "No browser could be opened. The link was copied to the clipboard:\n" + url, "Report Expert");
        }
    }

    /// <summary>Scrolls the page to the AI providers (used by Rex's "Open Settings").</summary>
    public void ShowProviders() => ProvidersSection?.BringIntoView();
}
