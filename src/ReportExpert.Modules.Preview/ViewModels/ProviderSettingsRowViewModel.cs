using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReportExpert.Modules.Copilot.Models;
using ReportExpert.Modules.Copilot.Services;

namespace ReportExpert.Modules.Preview.ViewModels;

/// <summary>
/// One configured AI provider in Settings: its key, model and endpoint, edited in place on the
/// shared <see cref="CopilotProviderProfile"/>.
/// </summary>
public sealed partial class ProviderSettingsRowViewModel : ObservableObject
{
    private static readonly HttpClient TestHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly Action _changed;
    private readonly Action<ProviderSettingsRowViewModel> _remove;
    private readonly Action<ProviderSettingsRowViewModel> _activate;
    private bool _loading = true;

    public ProviderSettingsRowViewModel(
        CopilotProviderProfile profile,
        bool isActive,
        Action changed,
        Action<ProviderSettingsRowViewModel> remove,
        Action<ProviderSettingsRowViewModel> activate)
    {
        Profile = profile;
        _changed = changed;
        _remove = remove;
        _activate = activate;

        foreach (var option in CopilotProviderDefaults.Models(profile.Provider))
            ModelOptions.Add(option);
        if (!ModelOptions.Any(m => string.Equals(m.ModelId, profile.EffectiveModel, StringComparison.OrdinalIgnoreCase)))
            ModelOptions.Insert(0, new AiModelOption(profile.EffectiveModel, profile.EffectiveModel, "Custom model"));

        _apiKey = profile.ApiKey;
        _model = profile.EffectiveModel;
        _baseUrl = profile.BaseUrl;
        _azureDeployment = profile.AzureDeployment;
        _isActive = isActive;
        _loading = false;
    }

    public CopilotProviderProfile Profile { get; }

    public CopilotProvider Provider => Profile.Provider;

    public string DisplayName => CopilotProviderDefaults.DisplayName(Provider);

    public ObservableCollection<AiModelOption> ModelOptions { get; } = [];

    /// <summary>Ollama runs locally and has no key.</summary>
    public bool NeedsKey => Provider != CopilotProvider.Ollama;

    /// <summary>Azure and Ollama need an endpoint; the others use their public one.</summary>
    public bool ShowsEndpoint => Provider is CopilotProvider.AzureOpenAI or CopilotProvider.Ollama;

    public bool IsAzure => Provider == CopilotProvider.AzureOpenAI;

    public string EndpointPlaceholder => CopilotProviderDefaults.DefaultBaseUrl(Provider);

    [ObservableProperty]
    private string _apiKey;

    [ObservableProperty]
    private string _model;

    [ObservableProperty]
    private string _baseUrl;

    [ObservableProperty]
    private string _azureDeployment;

    /// <summary>The provider Rex uses now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanActivate))]
    private bool _isActive;

    [ObservableProperty]
    private string _testMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    private bool _isTesting;

    /// <summary>Can be made the Rex provider: not already, and has a key that was not rejected.</summary>
    public bool CanActivate => !IsActive && Profile.IsUsable;

    /// <summary>"Working", "Key rejected", "No key", "Not tested yet".</summary>
    public string StatusText => !Profile.HasKey ? "No API key"
        : Profile.KeyStatus switch
        {
            CopilotKeyStatus.Valid => "Working",
            CopilotKeyStatus.Rejected => "Key rejected",
            _ => Provider == CopilotProvider.Ollama ? "Local" : "Not tested yet",
        };

    /// <summary>Ok, Warning or Error — picks the status chip colour.</summary>
    public string StatusKind => !Profile.HasKey ? "Warning"
        : Profile.KeyStatus switch
        {
            CopilotKeyStatus.Valid => "Ok",
            CopilotKeyStatus.Rejected => "Error",
            _ => "Neutral",
        };

    public string? KeyError => Profile.KeyStatus == CopilotKeyStatus.Rejected ? Profile.KeyError : null;

    public bool HasKeyError => !string.IsNullOrWhiteSpace(KeyError);

    partial void OnApiKeyChanged(string value)
    {
        if (_loading || string.Equals(Profile.ApiKey, value.Trim(), StringComparison.Ordinal))
            return;

        Profile.ApiKey = value.Trim();

        // A new key has not been tried yet; Rex may use it again.
        Profile.KeyStatus = CopilotKeyStatus.Unknown;
        Profile.KeyError = null;
        TestMessage = string.Empty;
        RefreshStatus();
        _changed();
    }

    partial void OnModelChanged(string value)
    {
        if (_loading || string.IsNullOrWhiteSpace(value))
            return;

        Profile.Model = value;
        _changed();
    }

    partial void OnBaseUrlChanged(string value)
    {
        if (_loading)
            return;

        Profile.BaseUrl = value.Trim();
        _changed();
    }

    partial void OnAzureDeploymentChanged(string value)
    {
        if (_loading)
            return;

        Profile.AzureDeployment = value.Trim();
        _changed();
    }

    public void RefreshStatus()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusKind));
        OnPropertyChanged(nameof(KeyError));
        OnPropertyChanged(nameof(HasKeyError));
        OnPropertyChanged(nameof(CanActivate));
    }

    [RelayCommand]
    private void Remove() => _remove(this);

    [RelayCommand]
    private void Activate() => _activate(this);

    [RelayCommand]
    private void GetKey()
    {
        try
        {
            Process.Start(new ProcessStartInfo(CopilotProviderDefaults.ApiKeyUrl(Provider)) { UseShellExecute = true });
        }
        catch (Exception)
        {
            TestMessage = "Could not open the API key page.";
        }
    }

    private bool CanTest() => !IsTesting;

    /// <summary>
    /// Checks the key with a tiny request, finds a model that answers (switching from a retired or
    /// unavailable one) and loads the models the key offers into the model list.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestAsync()
    {
        string key = Profile.ApiKey;
        if (NeedsKey && string.IsNullOrWhiteSpace(key))
        {
            TestMessage = "Enter an API key first.";
            return;
        }

        IsTesting = true;
        TestMessage = "Testing…";

        try
        {
            var report = await new ProviderProbe(TestHttp).TestAsync(
                Provider, Profile.EffectiveBaseUrl, key, Profile.EffectiveModel, Profile.AzureDeployment);

            if (report.KeyValid)
            {
                Profile.KeyStatus = CopilotKeyStatus.Valid;
                Profile.KeyError = null;
            }
            else if (report.KeyRejected)
            {
                Profile.KeyStatus = CopilotKeyStatus.Rejected;
                Profile.KeyError = report.Message;
            }

            LoadModelOptions(report.AvailableModels, report.WorkingModel);
            if (report.ModelChanged && !string.IsNullOrWhiteSpace(report.WorkingModel))
                Model = report.WorkingModel;

            TestMessage = report.Message;
        }
        catch (OperationCanceledException)
        {
            TestMessage = "Failed: the provider did not answer within 30 seconds. Check the endpoint and your connection.";
        }
        catch (HttpRequestException ex)
        {
            TestMessage = $"Failed: could not reach {DisplayName} ({ex.Message}).";
        }
        catch (Exception ex)
        {
            TestMessage = $"Failed: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
            RefreshStatus();
            _changed();
        }
    }

    /// <summary>
    /// The recommended models first (those the key offers, when its list is known), then the other chat
    /// models on the account, keeping the selected model in the list.
    /// </summary>
    private void LoadModelOptions(IReadOnlyList<ProviderModel> available, string? working)
    {
        if (available.Count == 0)
            return;

        var offered = new HashSet<string>(available.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        var options = new List<AiModelOption>();
        foreach (var option in CopilotProviderDefaults.Models(Provider))
        {
            if (offered.Contains(option.ModelId) || option.ModelId.EndsWith("-latest", StringComparison.OrdinalIgnoreCase))
                options.Add(option);
        }

        foreach (var model in available)
        {
            if (!options.Any(o => string.Equals(o.ModelId, model.Id, StringComparison.OrdinalIgnoreCase)))
                options.Add(new AiModelOption(model.DisplayName, model.Id, "Available on your account"));
        }

        string current = working ?? Model;
        if (!string.IsNullOrWhiteSpace(current) && !options.Any(o => string.Equals(o.ModelId, current, StringComparison.OrdinalIgnoreCase)))
            options.Insert(0, new AiModelOption(current, current, "Custom model"));

        // Keep the ComboBox selection: changing the list may clear SelectedValue, which would write "" back.
        bool wasLoading = _loading;
        _loading = true;
        string keep = Model;
        ModelOptions.Clear();
        foreach (var option in options)
            ModelOptions.Add(option);
        Model = keep;
        OnPropertyChanged(nameof(Model));
        _loading = wasLoading;
    }
}
