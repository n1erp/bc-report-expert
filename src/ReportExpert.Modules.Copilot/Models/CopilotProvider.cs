namespace ReportExpert.Modules.Copilot.Models;

/// <summary>Supported AI backend providers (all use OpenAI-compatible chat completions).</summary>
public enum CopilotProvider
{
    Groq,
    Gemini,
    Mistral,
    OpenRouter,
    Claude,
    OpenAI,
    AzureOpenAI,
    Ollama
}

/// <summary>A model the settings UI offers for a provider.</summary>
/// <param name="DisplayName">Short label for the combo box.</param>
/// <param name="ModelId">The id sent to the API.</param>
/// <param name="BestFor">One-line guidance under the id.</param>
public sealed record AiModelOption(string DisplayName, string ModelId, string BestFor);

public static class CopilotProviderDefaults
{
    public static string DefaultBaseUrl(CopilotProvider provider) => provider switch
    {
        CopilotProvider.Groq => "https://api.groq.com/openai/v1",
        CopilotProvider.Gemini => "https://generativelanguage.googleapis.com/v1beta/openai",
        CopilotProvider.Mistral => "https://api.mistral.ai/v1",
        CopilotProvider.OpenRouter => "https://openrouter.ai/api/v1",
        CopilotProvider.Claude => "https://api.anthropic.com/v1",
        CopilotProvider.OpenAI => "https://api.openai.com/v1",
        CopilotProvider.AzureOpenAI => "https://YOUR-RESOURCE.openai.azure.com/openai/deployments",
        CopilotProvider.Ollama => "http://localhost:11434/v1",
        _ => "https://api.groq.com/openai/v1"
    };

    /// <summary>The model a new profile starts with: the first recommended model of the provider.</summary>
    public static string DefaultModel(CopilotProvider provider) => Models(provider)[0].ModelId;

    public static string DisplayName(CopilotProvider provider) => provider switch
    {
        CopilotProvider.Groq => "Groq",
        CopilotProvider.Gemini => "Google Gemini",
        CopilotProvider.Mistral => "Mistral",
        CopilotProvider.OpenRouter => "OpenRouter",
        CopilotProvider.Claude => "Claude (Anthropic)",
        CopilotProvider.OpenAI => "OpenAI",
        CopilotProvider.AzureOpenAI => "Azure OpenAI",
        CopilotProvider.Ollama => "Ollama (local)",
        _ => provider.ToString()
    };

    /// <summary>Where the user creates an API key for this provider.</summary>
    public static string ApiKeyUrl(CopilotProvider provider) => provider switch
    {
        CopilotProvider.Groq => "https://console.groq.com/keys",
        CopilotProvider.Gemini => "https://aistudio.google.com/apikey",
        CopilotProvider.Mistral => "https://console.mistral.ai/api-keys",
        CopilotProvider.OpenRouter => "https://openrouter.ai/keys",
        CopilotProvider.Claude => "https://console.anthropic.com/settings/keys",
        CopilotProvider.OpenAI => "https://platform.openai.com/api-keys",
        CopilotProvider.AzureOpenAI => "https://portal.azure.com/",
        CopilotProvider.Ollama => "https://ollama.com/",
        _ => "https://console.groq.com/keys"
    };

    /// <summary>
    /// Recommended models, best default first (checked November 2026 against each provider's model list).
    /// Settings adds the other models the key's own <c>/models</c> endpoint offers, and Test falls back
    /// along this list when a model is retired, busy or not open to the account.
    /// </summary>
    public static IReadOnlyList<AiModelOption> Models(CopilotProvider provider) => provider switch
    {
        CopilotProvider.Gemini =>
        [
            new("Gemini 3.5 Flash", "gemini-3.5-flash", "Fast, dependable tool calling — best default for Rex (free tier)"),
            new("Gemini 3.8 Flash", "gemini-3.8-flash", "Newest Flash — strongest, but often busy at peak hours"),
            new("Gemini 3.7 Flash", "gemini-3.7-flash", "Previous Flash generation"),
            new("Gemini 3.5 Flash Lite", "gemini-3.5-flash-lite", "Cheapest and fastest, for simple asks"),
            new("Gemini 3.1 Flash Lite", "gemini-3.1-flash-lite", "Light model with generous free quota"),
            new("Gemini 3.1 Pro (preview)", "gemini-3.1-pro-preview", "Pro reasoning for complex edits — needs a paid plan"),
            new("Gemini Flash (latest)", "gemini-flash-latest", "Alias that always points at Google's current Flash"),
        ],
        CopilotProvider.Mistral =>
        [
            new("Mistral Large", "mistral-large-latest", "Mistral Large 4 — best quality and tool calling for Rex"),
            new("Mistral Medium", "mistral-medium-latest", "Balanced speed and quality"),
            new("Mistral Small", "mistral-small-latest", "Fast and inexpensive"),
            new("Codestral", "codestral-latest", "Code-oriented edits and explanations"),
        ],
        CopilotProvider.OpenRouter =>
        [
            new("Claude Sonnet 5.5", "anthropic/claude-sonnet-5.5", "Strong agent and tool use via OpenRouter (recommended)"),
            new("GPT-6.1 Sol", "openai/gpt-6.1-sol", "OpenAI's agentic all-rounder via OpenRouter"),
            new("Gemini 3.8 Flash", "google/gemini-3.8-flash", "Fast Gemini routed through OpenRouter"),
            new("DeepSeek V4.1 Flash", "deepseek/deepseek-v4.1-flash", "Very inexpensive, good tool calling"),
            new("Mistral Large 4", "mistralai/mistral-large-4-0", "Mistral via OpenRouter"),
            new("Grok 4.7", "x-ai/grok-4.7", "xAI via OpenRouter"),
            new("Kimi K3.1 Pro", "moonshotai/kimi-k3.1-pro", "Long-context agentic model via OpenRouter"),
        ],
        CopilotProvider.Claude =>
        [
            new("Claude Sonnet 5.5", "claude-sonnet-5-5", "Best default for Rex — fast and excellent with tools"),
            new("Claude Opus 5.5", "claude-opus-5-5", "Highest quality reasoning for complex layout work"),
            new("Claude Haiku 5.5", "claude-haiku-5-5", "Fastest and cheapest Claude"),
            new("Claude Fable 5.1", "claude-fable-5-1", "Most capable tier (Mythos-class, with extra safeguards)"),
        ],
        CopilotProvider.OpenAI =>
        [
            new("GPT-6.1 Sol", "gpt-6.1-sol", "Best default for Rex — agentic work and tool calling"),
            new("GPT-6.1 Terra", "gpt-6.1-terra", "Faster and cheaper tier"),
            new("GPT-6 Astra", "gpt-6-astra", "Flagship model for the hardest tasks"),
        ],
        CopilotProvider.AzureOpenAI =>
        [
            new("GPT-6.1 Sol", "gpt-6.1-sol", "Use your Azure deployment name if it differs"),
            new("GPT-6.1 Terra", "gpt-6.1-terra", "Faster and cheaper deployment"),
        ],
        CopilotProvider.Ollama =>
        [
            new("Qwen 3", "qwen3", "Local model with good tool calling"),
            new("GPT-OSS 20B", "gpt-oss:20b", "OpenAI open-weight model, runs locally"),
            new("Llama 3.2", "llama3.2", "Small local model"),
            new("Mistral", "mistral", "Local general-purpose"),
        ],
        _ => // Groq and fallback
        [
            new("OpenAI GPT-OSS 120B", "openai/gpt-oss-120b", "Best default for Rex on Groq — reasoning, coding, tools"),
            new("OpenAI GPT-OSS 20B", "openai/gpt-oss-20b", "Fast, inexpensive general-purpose tasks"),
            new("Qwen 3.6 27B (preview)", "qwen/qwen3.6-27b", "Preview model — evaluation only"),
        ],
    };

    /// <summary>All providers shown in the Settings combo box.</summary>
    public static IReadOnlyList<CopilotProvider> AllProviders { get; } =
    [
        CopilotProvider.Groq,
        CopilotProvider.Gemini,
        CopilotProvider.Mistral,
        CopilotProvider.OpenRouter,
        CopilotProvider.Claude,
        CopilotProvider.OpenAI,
        CopilotProvider.AzureOpenAI,
        CopilotProvider.Ollama,
    ];
}
