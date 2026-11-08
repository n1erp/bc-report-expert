using ReportExpert.Modules.Copilot.Models;
using ReportExpert.Modules.Copilot.Services;

namespace ReportExpert.Modules.Copilot.Tests;

public sealed class MultiProviderSettingsTests
{
    [Fact]
    public void Single_provider_settings_are_migrated_to_a_profile()
    {
        const string legacy = """
            {
              "Provider": "Gemini",
              "ApiKey": " AIza-old-key ",
              "Model": "gemini-2.5-pro",
              "BaseUrl": "https://generativelanguage.googleapis.com/v1beta/openai",
              "AzureDeployment": "",
              "AgentModeEnabled": true
            }
            """;

        var settings = CopilotSettingsService.Parse(legacy);

        var profile = Assert.Single(settings.Providers);
        Assert.Equal(CopilotProvider.Gemini, profile.Provider);
        Assert.Equal("AIza-old-key", profile.ApiKey);
        Assert.Equal("gemini-2.5-pro", profile.Model);
        Assert.Equal("AIza-old-key", settings.ApiKey);
        Assert.Equal("gemini-2.5-pro", settings.Model);
        Assert.True(settings.AgentModeEnabled);
        Assert.True(settings.HasUsableActiveProvider);
    }

    [Fact]
    public void Saved_file_has_profiles_and_no_legacy_keys()
    {
        var settings = new CopilotSettings { Provider = CopilotProvider.Claude };
        settings.GetOrAddProfile(CopilotProvider.Claude).ApiKey = "sk-ant";
        settings.GetOrAddProfile(CopilotProvider.Groq).ApiKey = "gsk";

        string json = CopilotSettingsService.Serialize(settings);
        var reloaded = CopilotSettingsService.Parse(json);

        Assert.DoesNotContain("\"ApiKey\": \"sk-ant\",\n  \"Model\"", json.Replace("\r\n", "\n"));
        Assert.Equal(2, reloaded.Providers.Count);
        Assert.Equal(CopilotProvider.Claude, reloaded.Provider);
        Assert.Equal("sk-ant", reloaded.ApiKey);
        Assert.Equal("gsk", reloaded.Profile(CopilotProvider.Groq)!.ApiKey);
    }

    [Fact]
    public void Legacy_file_without_a_key_creates_no_profile()
    {
        var settings = CopilotSettingsService.Parse("""{ "Provider": "Groq", "ApiKey": "", "Model": "llama-3.3-70b-versatile" }""");

        Assert.Empty(settings.Providers);
        Assert.False(settings.HasUsableActiveProvider);
    }

    [Fact]
    public void Rejected_active_provider_hands_over_to_the_next_usable_one()
    {
        var settings = new CopilotSettings { Provider = CopilotProvider.Gemini };
        settings.GetOrAddProfile(CopilotProvider.Gemini).ApiKey = "bad";
        settings.GetOrAddProfile(CopilotProvider.Mistral);              // no key: skipped
        settings.GetOrAddProfile(CopilotProvider.OpenAI).ApiKey = "sk-ok";

        settings.Profile(CopilotProvider.Gemini)!.KeyStatus = CopilotKeyStatus.Rejected;

        Assert.False(settings.HasUsableActiveProvider);
        Assert.True(settings.EnsureUsableActive());
        Assert.Equal(CopilotProvider.OpenAI, settings.Provider);
        Assert.Equal(["sk-ok"], settings.UsableProviders.Select(p => p.ApiKey));
    }

    [Fact]
    public void Nothing_usable_leaves_the_active_provider_alone()
    {
        var settings = new CopilotSettings { Provider = CopilotProvider.Claude };
        settings.GetOrAddProfile(CopilotProvider.Claude);

        Assert.False(settings.EnsureUsableActive());
        Assert.Equal(CopilotProvider.Claude, settings.Provider);
    }

    [Fact]
    public void Ollama_needs_no_key_and_model_choice_is_per_provider()
    {
        var settings = new CopilotSettings { Provider = CopilotProvider.Ollama };
        settings.GetOrAddProfile(CopilotProvider.Ollama);
        settings.Model = "qwen2.5";
        settings.GetOrAddProfile(CopilotProvider.Groq).ApiKey = "gsk";

        Assert.True(settings.HasUsableActiveProvider);
        Assert.Equal("qwen2.5", settings.Model);
        Assert.Equal(CopilotProviderDefaults.DefaultModel(CopilotProvider.Groq), settings.Profile(CopilotProvider.Groq)!.EffectiveModel);
    }

    [Fact]
    public void Key_rejection_is_recognised_from_the_status_code()
    {
        Assert.True(new CopilotApiException(CopilotProvider.Gemini, 401, "bad key", "x").IsKeyRejected);
        Assert.True(new CopilotApiException(CopilotProvider.Gemini, 403, "forbidden", "x").IsKeyRejected);
        Assert.False(new CopilotApiException(CopilotProvider.Gemini, 429, "slow down", "x").IsKeyRejected);
    }
}
