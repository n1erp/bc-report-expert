using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReportExpert.Modules.Copilot.Models;

namespace ReportExpert.Modules.Copilot.Services;

/// <summary>
/// Unified HTTP client for Groq, Gemini, Mistral, OpenRouter, Claude, OpenAI, Azure, and Ollama
/// (all expose an OpenAI-compatible <c>/chat/completions</c> endpoint).
/// </summary>
public sealed class OpenAiCompatibleClient : IChatCompletionClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    private readonly CopilotSettingsService _settingsService;

    public OpenAiCompatibleClient(CopilotSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        var completion = await CompleteAsync(new ChatCompletionRequest(messages), cancellationToken);
        return completion.Content;
    }

    public async Task<ChatCompletion> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = _settingsService.Settings;
        if (settings.Provider != CopilotProvider.Ollama && string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException($"No API key is set for {CopilotProviderDefaults.DisplayName(settings.Provider)}. Add one in Settings › Copilot AI.");

        string url = BuildCompletionsUrl(settings);

        var payload = new JsonObject
        {
            ["model"] = settings.Model,
            ["messages"] = BuildMessages(request.Messages),
            ["temperature"] = 0.2
        };

        if (request.Tools.Count > 0)
        {
            payload["tools"] = BuildTools(request.Tools);
            payload["tool_choice"] = request.ToolChoice;

            // Groq rejects the whole completion when the model names a tool that is not in
            // request.tools. Prefer recovering in the agent loop over failing the turn.
            if (settings.Provider == CopilotProvider.Groq)
                payload["disable_tool_validation"] = true;
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, url);
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        if (settings.Provider == CopilotProvider.AzureOpenAI)
            message.Headers.Add("api-key", settings.ApiKey);

        // OpenRouter ranks apps that identify themselves; optional but improves rate limits.
        if (settings.Provider == CopilotProvider.OpenRouter)
        {
            message.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/n1erp/bc-report-expert");
            message.Headers.TryAddWithoutValidation("X-Title", "Report Expert");
        }

        message.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        var (status, success, body) = await SendAsync(message, cancellationToken);

        // Reasoning models (OpenAI GPT-5+/o-series, some Gemini and Claude settings) only accept the
        // default temperature; send the request again without it rather than failing the turn.
        if (!success && status == 400 && (ProviderProbe.ErrorText(body) ?? body).Contains("temperature", StringComparison.OrdinalIgnoreCase))
        {
            payload.Remove("temperature");
            using var retry = new HttpRequestMessage(HttpMethod.Post, url);
            foreach (var header in message.Headers)
                retry.Headers.TryAddWithoutValidation(header.Key, header.Value);
            retry.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            (status, success, body) = await SendAsync(retry, cancellationToken);
        }

        if (!success)
        {
            string detail = ProviderProbe.ErrorText(body) ?? body;
            if (ProviderProbe.Classify(status, detail) == ProbeOutcome.ModelUnavailable)
            {
                detail = $"The model '{settings.Model}' is not available for this key (it may be retired or not open to your account). " +
                         $"Open Settings › AI providers and press Test: Rex picks a model that works. Provider said: {detail}";
            }

            // Most failures with an image attached come from a text-only model.
            string hint = request.Messages.Any(m => m.Images is { Count: > 0 })
                ? $"\n\nIf the model '{settings.Model}' does not accept images, pick a vision-capable one " +
                  "(for example Gemini 2.5 Flash, GPT-4o, or Claude Sonnet) or remove the image."
                : string.Empty;
            throw new CopilotApiException(
                settings.Provider,
                status,
                detail,
                $"{CopilotProviderDefaults.DisplayName(settings.Provider)} API error ({status}): {detail}{hint}");
        }

        return ParseCompletion(body);
    }

    private static async Task<(int Status, bool Success, string Body)> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await Http.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        return ((int)response.StatusCode, response.IsSuccessStatusCode, body);
    }

    internal static JsonArray BuildMessages(IReadOnlyList<ChatMessage> messages)
    {
        var array = new JsonArray();

        foreach (var message in messages)
        {
            var node = new JsonObject
            {
                ["role"] = message.Role,
                ["content"] = BuildContent(message)
            };

            if (message.ToolCallId is not null)
                node["tool_call_id"] = message.ToolCallId;

            if (message.Name is not null)
                node["name"] = message.Name;

            if (message.ToolCalls is { Count: > 0 } calls)
            {
                var callArray = new JsonArray();

                foreach (var call in calls)
                {
                    var callNode = new JsonObject
                    {
                        ["id"] = call.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = call.Name,
                            ["arguments"] = call.ArgumentsJson
                        }
                    };

                    // Gemini's thought signature must be returned exactly as received.
                    if (call.ExtraContentJson is { Length: > 0 } extra)
                        callNode["extra_content"] = JsonNode.Parse(extra);

                    callArray.Add(callNode);
                }

                node["tool_calls"] = callArray;
            }

            array.Add(node);
        }

        return array;
    }

    /// <summary>
    /// A plain string, or — when the message carries images — the multimodal part list every
    /// OpenAI-compatible provider accepts: a text part followed by one <c>image_url</c> part per image.
    /// </summary>
    private static JsonNode BuildContent(ChatMessage message)
    {
        if (message.Images is not { Count: > 0 } images)
            return JsonValue.Create(message.Content)!;

        var parts = new JsonArray();
        if (message.Content.Length > 0)
            parts.Add(new JsonObject { ["type"] = "text", ["text"] = message.Content });

        foreach (var image in images)
        {
            parts.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject { ["url"] = image.DataUrl }
            });
        }

        return parts;
    }

    private static JsonArray BuildTools(IReadOnlyList<ChatTool> tools)
    {
        var array = new JsonArray();

        foreach (var tool in tools)
        {
            array.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JsonNode.Parse(tool.ParametersSchema.GetRawText())
                }
            });
        }

        return array;
    }

    private static ChatCompletion ParseCompletion(string body)
    {
        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return new ChatCompletion(string.Empty, []);

        var message = choices[0].GetProperty("message");

        string content = message.TryGetProperty("content", out var contentElement)
            ? contentElement.GetString() ?? string.Empty
            : string.Empty;

        if (!message.TryGetProperty("tool_calls", out var toolCalls) || toolCalls.ValueKind != JsonValueKind.Array)
            return new ChatCompletion(content, []);

        var calls = new List<ChatToolCall>(toolCalls.GetArrayLength());

        foreach (var call in toolCalls.EnumerateArray())
        {
            if (!call.TryGetProperty("function", out var function))
                continue;

            string name = function.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? string.Empty
                : string.Empty;

            if (name.Length == 0)
                continue;

            // Arguments arrive as a JSON string rather than an object, and an empty one is
            // normal for a tool whose parameters are all optional.
            string arguments = function.TryGetProperty("arguments", out var argumentsElement)
                ? argumentsElement.GetString() ?? "{}"
                : "{}";

            string id = call.TryGetProperty("id", out var idElement)
                ? idElement.GetString() ?? string.Empty
                : string.Empty;

            string? extraContent = call.TryGetProperty("extra_content", out var extraElement) &&
                                   extraElement.ValueKind == JsonValueKind.Object
                ? extraElement.GetRawText()
                : null;

            calls.Add(new ChatToolCall(id, name, arguments.Length == 0 ? "{}" : arguments)
            {
                ExtraContentJson = extraContent
            });
        }

        return new ChatCompletion(content, calls);
    }

    private static string BuildCompletionsUrl(CopilotSettings settings)
    {
        string baseUrl = settings.BaseUrl.TrimEnd('/');

        if (settings.Provider == CopilotProvider.AzureOpenAI)
        {
            string deployment = string.IsNullOrWhiteSpace(settings.AzureDeployment)
                ? settings.Model
                : settings.AzureDeployment;
            return $"{baseUrl}/{deployment}/chat/completions?api-version=2024-02-15-preview";
        }

        return $"{baseUrl}/chat/completions";
    }
}

/// <summary>A provider answered with an error status.</summary>
public sealed class CopilotApiException(CopilotProvider provider, int statusCode, string detail, string message)
    : HttpRequestException(message)
{
    public CopilotProvider Provider { get; } = provider;

    public int Status { get; } = statusCode;

    /// <summary>The provider's own error text.</summary>
    public string Detail { get; } = detail;

    /// <summary>The provider refused the key itself (as opposed to a bad request or an outage).</summary>
    public bool IsKeyRejected => Status is 401 or 403;
}
