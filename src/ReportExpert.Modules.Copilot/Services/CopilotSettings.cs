using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReportExpert.Common;
using ReportExpert.Modules.Copilot.Models;

namespace ReportExpert.Modules.Copilot.Services;

/// <summary>Whether a provider's API key is known to work.</summary>
public enum CopilotKeyStatus
{
    /// <summary>Not tested yet; Rex will try it.</summary>
    Unknown,

    /// <summary>A test or a chat turn succeeded with it.</summary>
    Valid,

    /// <summary>The provider rejected it (401/403). Rex will not use it until it is changed or re-tested.</summary>
    Rejected,
}

/// <summary>One configured AI provider: its key, endpoint, and the model last chosen for it.</summary>
public sealed class CopilotProviderProfile
{
    public CopilotProvider Provider { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string AzureDeployment { get; set; } = string.Empty;
    public CopilotKeyStatus KeyStatus { get; set; }

    /// <summary>The provider's error for a rejected key, shown in Settings and in the Rex picker.</summary>
    public string? KeyError { get; set; }

    /// <summary>The model to use, falling back to the provider default.</summary>
    [JsonIgnore]
    public string EffectiveModel => string.IsNullOrWhiteSpace(Model) ? CopilotProviderDefaults.DefaultModel(Provider) : Model;

    [JsonIgnore]
    public string EffectiveBaseUrl => string.IsNullOrWhiteSpace(BaseUrl) ? CopilotProviderDefaults.DefaultBaseUrl(Provider) : BaseUrl;

    /// <summary>A key is entered (Ollama runs locally and needs none).</summary>
    [JsonIgnore]
    public bool HasKey => Provider == CopilotProvider.Ollama || !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Rex can send with it: a key is entered and the provider has not rejected it.</summary>
    [JsonIgnore]
    public bool IsUsable => HasKey && KeyStatus != CopilotKeyStatus.Rejected;
}

/// <summary>User-configurable AI provider settings persisted to disk.</summary>
/// <remarks>
/// Several providers can be configured at once, each with its own key and model. <see cref="Provider"/>
/// is the one Rex uses; the Rex picker switches between the usable ones.
/// </remarks>
public sealed class CopilotSettings
{
    /// <summary>The provider Rex sends to.</summary>
    public CopilotProvider Provider { get; set; } = CopilotProvider.Groq;

    /// <summary>Every provider the user has set up, in the order they were added.</summary>
    public List<CopilotProviderProfile> Providers { get; set; } = [];

    public bool AgentModeEnabled { get; set; }

    /// <summary>The active provider's profile, or <see langword="null"/> when it is not set up.</summary>
    [JsonIgnore]
    public CopilotProviderProfile? Active => Profile(Provider);

    /// <summary>The active provider's key.</summary>
    [JsonIgnore]
    public string ApiKey => Active?.ApiKey ?? string.Empty;

    /// <summary>The active provider's model; setting it records the choice on that provider.</summary>
    [JsonIgnore]
    public string Model
    {
        get => Active?.EffectiveModel ?? CopilotProviderDefaults.DefaultModel(Provider);
        set => GetOrAddProfile(Provider).Model = value;
    }

    [JsonIgnore]
    public string BaseUrl => Active?.EffectiveBaseUrl ?? CopilotProviderDefaults.DefaultBaseUrl(Provider);

    [JsonIgnore]
    public string AzureDeployment => Active?.AzureDeployment ?? string.Empty;

    /// <summary>Providers Rex can send with, in Settings order.</summary>
    [JsonIgnore]
    public IEnumerable<CopilotProviderProfile> UsableProviders => Providers.Where(p => p.IsUsable);

    /// <summary>Whether Rex has a provider it can send with right now.</summary>
    [JsonIgnore]
    public bool HasUsableActiveProvider => Active is { IsUsable: true };

    public CopilotProviderProfile? Profile(CopilotProvider provider) =>
        Providers.FirstOrDefault(p => p.Provider == provider);

    public CopilotProviderProfile GetOrAddProfile(CopilotProvider provider)
    {
        if (Profile(provider) is { } existing)
            return existing;

        var profile = new CopilotProviderProfile { Provider = provider, Model = CopilotProviderDefaults.DefaultModel(provider) };
        Providers.Add(profile);
        return profile;
    }

    /// <summary>
    /// Keeps <see cref="Provider"/> pointing at something usable: when the active provider has no key
    /// or a rejected one, the first usable provider takes over.
    /// </summary>
    /// <returns>Whether the active provider changed.</returns>
    public bool EnsureUsableActive()
    {
        if (HasUsableActiveProvider)
            return false;

        if (UsableProviders.FirstOrDefault() is not { } next)
            return false;

        Provider = next.Provider;
        return true;
    }

    // ---- Settings written before several providers were supported (one provider, one key). ----

    [JsonPropertyName("ApiKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyApiKey { get; set; }

    [JsonPropertyName("Model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyModel { get; set; }

    [JsonPropertyName("BaseUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyBaseUrl { get; set; }

    [JsonPropertyName("AzureDeployment")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyAzureDeployment { get; set; }

    /// <summary>Moves single-provider settings into <see cref="Providers"/>.</summary>
    /// <returns>Whether anything was migrated (the file should be re-saved).</returns>
    public bool MigrateLegacy()
    {
        bool hadLegacy = LegacyApiKey is not null || LegacyModel is not null || LegacyBaseUrl is not null || LegacyAzureDeployment is not null;
        if (!hadLegacy)
            return false;

        if (Profile(Provider) is null && (!string.IsNullOrWhiteSpace(LegacyApiKey) || Provider == CopilotProvider.Ollama))
        {
            Providers.Add(new CopilotProviderProfile
            {
                Provider = Provider,
                ApiKey = LegacyApiKey?.Trim() ?? string.Empty,
                Model = string.IsNullOrWhiteSpace(LegacyModel) ? CopilotProviderDefaults.DefaultModel(Provider) : LegacyModel,
                BaseUrl = LegacyBaseUrl ?? string.Empty,
                AzureDeployment = LegacyAzureDeployment ?? string.Empty,
            });
        }

        LegacyApiKey = LegacyModel = LegacyBaseUrl = LegacyAzureDeployment = null;
        return true;
    }

    /// <summary>
    /// Qualified MCP tool names (<c>server__tool</c>) the user has turned off in Settings.
    /// Disabled tools are hidden from the model and cannot be executed.
    /// </summary>
    public List<string> DisabledMcpTools { get; set; } = [];

    /// <summary>Whether the given qualified tool name is allowed to run.</summary>
    public bool IsMcpToolEnabled(string qualifiedName)
    {
        if (string.IsNullOrEmpty(qualifiedName) || DisabledMcpTools.Count == 0)
            return true;

        return !DisabledMcpTools.Contains(qualifiedName, StringComparer.Ordinal);
    }

    /// <summary>Enables or disables a tool and returns whether the set changed.</summary>
    public bool SetMcpToolEnabled(string qualifiedName, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
            return false;

        if (enabled)
            return DisabledMcpTools.RemoveAll(name =>
                string.Equals(name, qualifiedName, StringComparison.Ordinal)) > 0;

        if (DisabledMcpTools.Contains(qualifiedName, StringComparer.Ordinal))
            return false;

        DisabledMcpTools.Add(qualifiedName);
        return true;
    }
}

/// <summary>
/// Persists <see cref="CopilotSettings"/> to
/// <c>%AppData%\ReportExpert\copilot.json</c> (local only — never commit).
/// </summary>
public sealed class CopilotSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _settingsPath;

    public CopilotSettings Settings { get; private set; } = new();

    public CopilotSettingsService()
    {
        _settingsPath = AppConstants.GetAppDataPath(AppConstants.CopilotSettingsFileName);
        Load();
    }

    public void Load()
    {
        if (!File.Exists(_settingsPath))
        {
            Settings = new CopilotSettings();
            return;
        }

        try
        {
            Settings = JsonSerializer.Deserialize<CopilotSettings>(File.ReadAllText(_settingsPath), JsonOptions)
                       ?? new CopilotSettings();
            if (Settings.MigrateLegacy())
                Save();
        }
        catch (Exception)
        {
            // Prefer defaults over crashing startup on a corrupt Copilot settings file.
            Settings = new CopilotSettings();
        }
    }

    /// <summary>Raised after <see cref="Save"/>, so Rex and Settings stay in step when either changes providers.</summary>
    public event Action? Saved;

    public void Save()
    {
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(Settings, JsonOptions));
        Saved?.Invoke();
    }

    /// <summary>Reads settings from JSON text (tests, and the file loader).</summary>
    internal static CopilotSettings Parse(string json)
    {
        var settings = JsonSerializer.Deserialize<CopilotSettings>(json, JsonOptions) ?? new CopilotSettings();
        settings.MigrateLegacy();
        return settings;
    }

    /// <summary>Writes settings as JSON text.</summary>
    internal static string Serialize(CopilotSettings settings) => JsonSerializer.Serialize(settings, JsonOptions);
}
