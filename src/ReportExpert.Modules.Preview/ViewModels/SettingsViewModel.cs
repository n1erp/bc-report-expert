using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReportExpert.Common;
using ReportExpert.Mcp.Client;
using ReportExpert.Modules.Copilot.Models;
using ReportExpert.Modules.Copilot.Services;
using ReportExpert.Modules.Preview.Services;
using Wpf.Ui.Controls;

namespace ReportExpert.Modules.Preview.ViewModels;

public partial class SettingsViewModel : ObservableObject
{

    private readonly SettingsService _settingsService;
    private readonly CopilotSettingsService _copilotSettingsService;
    private bool _suppressAutoSave = true;
    private CancellationTokenSource? _autoSaveCts;
    private IMcpServerHost? _mcpHost;
    private Func<Task>? _restartMcp;
    private Action? _onToolEnablementChanged;

    [ObservableProperty]
    private string _theme;

    [ObservableProperty]
    private int _rowsGenerated;

    [ObservableProperty]
    private int _fontSize;

    [ObservableProperty]
    private int _maxRecentFiles;

    [ObservableProperty]
    private bool _rememberRecentFiles;

    [ObservableProperty]
    private bool _autoPreview;

    [ObservableProperty]
    private bool _alSourcePanelVisible;

    [ObservableProperty]
    private bool _showDesignerRulers = true;

    [ObservableProperty]
    private bool _showDesignerGridlines = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _agentModeEnabled;

    [ObservableProperty]
    private bool _isRefreshingMcp;

    [ObservableProperty]
    private string _mcpConfigurationPath = string.Empty;

    /// <summary>The MCP servers and whether each is running.</summary>
    public ObservableCollection<McpServerRowViewModel> McpServers { get; } = [];

    /// <summary>The AI providers set up for Rex, each with its own key and model.</summary>
    public ObservableCollection<ProviderSettingsRowViewModel> ConfiguredProviders { get; } = [];

    /// <summary>Providers not set up yet, offered by "Add provider".</summary>
    public ObservableCollection<CopilotProvider> AddableProviders { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddProviderCommand))]
    private CopilotProvider? _providerToAdd;

    public bool HasConfiguredProviders => ConfiguredProviders.Count > 0;

    public bool HasAddableProviders => AddableProviders.Count > 0;

    public string[] ThemeOptions { get; } = ThemeNames.All;

    /// <summary>Settings › Preview › Sample rows: quick picks; any other value is typed in the number box.</summary>
    public IReadOnlyList<RowPresetOption> RowPresets { get; } =
        [new(10), new(25), new(50), new(100), new(250)];

    /// <summary>"50 rows per dataset" — the summary beside the heading.</summary>
    public string RowsSummary => RowsGenerated == 1 ? "1 row per dataset" : $"{RowsGenerated} rows per dataset";

    public event Action? SettingsSaved;

    public SettingsViewModel(SettingsService settingsService, CopilotSettingsService copilotSettingsService)
    {
        _settingsService = settingsService;
        _copilotSettingsService = copilotSettingsService;

        var s = settingsService.Settings;
        _theme = s.Theme;
        _rowsGenerated = Math.Clamp(s.RowsGenerated, 1, 500);
        SelectPreset(_rowsGenerated);
        _fontSize = s.FontSize;
        _maxRecentFiles = s.MaxRecentFiles;
        _rememberRecentFiles = s.RememberRecentFiles;
        _autoPreview = s.AutoPreview;
        _alSourcePanelVisible = s.AlSourcePanelVisible;
        _showDesignerRulers = s.ShowDesignerRulers;
        _showDesignerGridlines = s.ShowDesignerGridlines;

        var c = copilotSettingsService.Settings;
        _agentModeEnabled = c.AgentModeEnabled;
        RebuildProviderRows();

        _suppressAutoSave = false;
    }

    /// <summary>
    /// Connects the MCP section to the running servers.
    /// </summary>
    /// <param name="host">The server host, or <see langword="null"/> when the shell has none.</param>
    /// <param name="restart">Restarts the servers and reconnects the assistant.</param>
    /// <param name="onToolEnablementChanged">
    /// Called when the user toggles a tool so the assistant picks up the new filter immediately.
    /// </param>
    public void AttachMcp(
        IMcpServerHost? host,
        Func<Task>? restart = null,
        Action? onToolEnablementChanged = null)
    {
        _mcpHost = host;
        _restartMcp = restart;
        _onToolEnablementChanged = onToolEnablementChanged;
        McpConfigurationPath = host?.ConfigurationPath ?? string.Empty;

        _ = RefreshMcpAsync();
    }

    /// <summary>
    /// Pulls Copilot provider/model/key from the shared settings service (e.g. after Rex chat changes the model).
    /// </summary>
    public void ReloadCopilotFromService()
    {
        _suppressAutoSave = true;
        try
        {
            var c = _copilotSettingsService.Settings;
            AgentModeEnabled = c.AgentModeEnabled;
            RebuildProviderRows();
        }
        finally
        {
            _suppressAutoSave = false;
        }
    }

    /// <summary>Re-reads the state of each MCP server.</summary>
    [RelayCommand]
    private async Task RefreshMcpAsync()
    {
        if (_mcpHost is null)
            return;

        IsRefreshingMcp = true;

        try
        {
            var statuses = await _mcpHost.DescribeAsync();
            var expandedIds = McpServers
                .Where(server => server.IsExpanded)
                .Select(server => server.Id)
                .ToHashSet(StringComparer.Ordinal);

            McpServers.Clear();
            foreach (var status in statuses)
            {
                var row = new McpServerRowViewModel(
                    status,
                    _copilotSettingsService,
                    OnToolEnablementChanged);

                if (expandedIds.Contains(row.Id))
                    row.IsExpanded = true;

                McpServers.Add(row);
            }
        }
        catch (Exception)
        {
            // The list is diagnostic. Failing to read it must not take the settings screen down.
            McpServers.Clear();
        }
        finally
        {
            IsRefreshingMcp = false;
        }
    }

    /// <summary>Restarts the MCP servers, picking up any hand edits to the configuration.</summary>
    [RelayCommand]
    private async Task RestartMcpAsync()
    {
        if (_restartMcp is null)
            return;

        IsRefreshingMcp = true;

        try
        {
            await _restartMcp();
        }
        finally
        {
            IsRefreshingMcp = false;
        }

        await RefreshMcpAsync();
    }

    /// <summary>Opens the server list in the user's editor.</summary>
    [RelayCommand]
    private void EditMcpConfiguration()
    {
        if (McpConfigurationPath.Length == 0)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(McpConfigurationPath) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No association for .json, or the file was removed. Nothing useful to do about it.
        }
    }

    private void OnToolEnablementChanged()
    {
        foreach (var server in McpServers)
            server.RefreshEnabledCount();

        _onToolEnablementChanged?.Invoke();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_suppressAutoSave)
            return;

        if (e.PropertyName is nameof(StatusMessage)
            or nameof(RowsSummary) or nameof(IsRefreshingMcp) or nameof(McpConfigurationPath)
            or nameof(ProviderToAdd) or nameof(HasConfiguredProviders) or nameof(HasAddableProviders) or null)
            return;

        if (e.PropertyName == nameof(RowsGenerated))
        {
            int clamped = Math.Clamp(RowsGenerated, 1, 500);
            if (clamped != RowsGenerated)
            {
                RowsGenerated = clamped;
                return;
            }

            SelectPreset(clamped);
            OnPropertyChanged(nameof(RowsSummary));
        }

        ScheduleAutoSave();
    }

    [RelayCommand]
    private void SetRowCount(object? preset)
    {
        int rows = preset switch
        {
            int value => value,
            RowPresetOption option => option.Rows,
            string text when int.TryParse(text, out int parsed) => parsed,
            _ => 0,
        };
        if (rows > 0)
            RowsGenerated = Math.Clamp(rows, 1, 500);
    }

    private void SelectPreset(int rows)
    {
        foreach (var preset in RowPresets)
            preset.IsSelected = preset.Rows == rows;
    }

    private void ScheduleAutoSave()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;

        _ = PersistAfterDelayAsync(token);
    }

    private async Task PersistAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(350, token);
            Persist();
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer change
        }
    }

    private void Persist()
    {
        _settingsService.Settings.Theme = Theme;
        _settingsService.Settings.RowsGenerated = Math.Clamp(RowsGenerated, 1, 500);
        _settingsService.Settings.FontSize = FontSize;
        _settingsService.Settings.MaxRecentFiles = Math.Clamp(MaxRecentFiles, 1, 50);
        _settingsService.Settings.RememberRecentFiles = RememberRecentFiles;
        _settingsService.Settings.AutoPreview = AutoPreview;
        _settingsService.Settings.AlSourcePanelVisible = AlSourcePanelVisible;
        _settingsService.Settings.ShowDesignerRulers = ShowDesignerRulers;
        _settingsService.Settings.ShowDesignerGridlines = ShowDesignerGridlines;
        _settingsService.Save();

        // Provider rows edit their profiles in place; saving writes them along with agent mode.
        var copilot = _copilotSettingsService.Settings;
        copilot.AgentModeEnabled = AgentModeEnabled;
        copilot.EnsureUsableActive();
        _copilotSettingsService.Save();
        foreach (var row in ConfiguredProviders)
        {
            row.IsActive = row.Provider == copilot.Provider && row.Profile.IsUsable;
            row.RefreshStatus();
        }

        StatusMessage = "Saved";
        SettingsSaved?.Invoke();
    }

    // ---- AI providers ----

    private void RebuildProviderRows()
    {
        var settings = _copilotSettingsService.Settings;
        ConfiguredProviders.Clear();
        foreach (var profile in settings.Providers)
        {
            ConfiguredProviders.Add(new ProviderSettingsRowViewModel(
                profile,
                isActive: profile.Provider == settings.Provider && profile.IsUsable,
                changed: ScheduleAutoSave,
                remove: RemoveProvider,
                activate: ActivateProvider));
        }

        AddableProviders.Clear();
        foreach (var provider in CopilotProviderDefaults.AllProviders.Where(p => settings.Profile(p) is null))
            AddableProviders.Add(provider);
        ProviderToAdd = AddableProviders.Count > 0 ? AddableProviders[0] : null;

        OnPropertyChanged(nameof(HasConfiguredProviders));
        OnPropertyChanged(nameof(HasAddableProviders));
    }

    private bool CanAddProvider() => ProviderToAdd is not null;

    /// <summary>Adds the chosen provider; the first one added becomes the one Rex uses.</summary>
    [RelayCommand(CanExecute = nameof(CanAddProvider))]
    private void AddProvider()
    {
        if (ProviderToAdd is not { } provider)
            return;

        var settings = _copilotSettingsService.Settings;
        bool first = !settings.UsableProviders.Any();
        settings.GetOrAddProfile(provider);
        if (first)
            settings.Provider = provider;

        RebuildProviderRows();
        _copilotSettingsService.Save();
        StatusMessage = $"{CopilotProviderDefaults.DisplayName(provider)} added — enter its API key.";
    }

    private void RemoveProvider(ProviderSettingsRowViewModel row)
    {
        var settings = _copilotSettingsService.Settings;
        settings.Providers.Remove(row.Profile);
        settings.EnsureUsableActive();
        RebuildProviderRows();
        _copilotSettingsService.Save();
        StatusMessage = $"{row.DisplayName} removed.";
    }

    private void ActivateProvider(ProviderSettingsRowViewModel row)
    {
        if (!row.Profile.IsUsable)
            return;

        _copilotSettingsService.Settings.Provider = row.Provider;
        _copilotSettingsService.Save();
        foreach (var other in ConfiguredProviders)
            other.IsActive = ReferenceEquals(other, row);
        StatusMessage = $"Rex now uses {row.DisplayName}.";
    }
}

/// <summary>A quick pick for the sample row count.</summary>
public sealed partial class RowPresetOption(int rows) : ObservableObject
{
    public int Rows { get; } = rows;

    public string Label => Rows.ToString(System.Globalization.CultureInfo.CurrentCulture);

    [ObservableProperty]
    private bool _isSelected;
}
