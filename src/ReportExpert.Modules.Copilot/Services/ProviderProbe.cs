using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReportExpert.Modules.Copilot.Models;

namespace ReportExpert.Modules.Copilot.Services;

/// <summary>A model a provider offers to this key, as its <c>/models</c> endpoint lists it.</summary>
public sealed record ProviderModel(string Id, string DisplayName);

/// <summary>How one test request ended.</summary>
public enum ProbeOutcome
{
    /// <summary>The model answered.</summary>
    Works,

    /// <summary>The provider refused the key (wrong, revoked or not allowed).</summary>
    KeyRejected,

    /// <summary>The key is fine but this model is retired, unknown or not open to this account.</summary>
    ModelUnavailable,

    /// <summary>The key is fine but the model is overloaded right now (503, 529, 500).</summary>
    Busy,

    /// <summary>The key is fine but the account's quota or rate limit is used up for this model (429).</summary>
    QuotaExceeded,

    /// <summary>Anything else (network, bad request).</summary>
    Failed,
}

public sealed record ProbeResult(string Model, ProbeOutcome Outcome, int Status, string Message)
{
    /// <summary>The request got past authentication, so the key itself is valid.</summary>
    public bool KeyAccepted => Outcome is ProbeOutcome.Works or ProbeOutcome.ModelUnavailable or ProbeOutcome.Busy or ProbeOutcome.QuotaExceeded;
}

/// <summary>The outcome of Settings' Test: whether the key works, which model to use, and the models offered.</summary>
public sealed record ProviderTestReport(
    bool KeyValid,
    bool KeyRejected,
    string? WorkingModel,
    bool ModelChanged,
    string Message,
    IReadOnlyList<ProviderModel> AvailableModels,
    IReadOnlyList<ProbeResult> Attempts);

/// <summary>
/// Tests a provider key and finds a model that works. Providers retire models quickly and some models are
/// only open to some accounts (paid plans, older accounts), so a "model not available", "quota" or "busy"
/// answer does not mean the key is wrong: the probe then tries the provider's recommended models, newest
/// first, among those the key's own <c>/models</c> list offers, and reports the first one that answers.
/// </summary>
public sealed class ProviderProbe(HttpClient http)
{
    private const int MaxFallbackAttempts = 5;

    /// <summary>Sends a one-line chat request to a model.</summary>
    public async Task<ProbeResult> ProbeModelAsync(CopilotProvider provider, string baseUrl, string apiKey, string model, string? azureDeployment = null, CancellationToken cancellationToken = default)
    {
        var url = provider == CopilotProvider.AzureOpenAI
            ? $"{baseUrl.TrimEnd('/')}/{(string.IsNullOrWhiteSpace(azureDeployment) ? model : azureDeployment)}/chat/completions?api-version=2024-02-15-preview"
            : $"{baseUrl.TrimEnd('/')}/chat/completions";

        // No max_tokens or temperature: newer reasoning models reject them, and the answer is one word anyway.
        var payload = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Reply with OK only." }),
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            Authorize(request, provider, apiKey);
            request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                return new ProbeResult(model, ProbeOutcome.Works, status, "");
            }

            var message = ErrorText(body) ?? Trim(body);
            return new ProbeResult(model, Classify(status, message), status, message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeResult(model, ProbeOutcome.Busy, 0, "The model did not answer in time.");
        }
        catch (HttpRequestException exception)
        {
            return new ProbeResult(model, ProbeOutcome.Failed, 0, exception.Message);
        }
    }

    /// <summary>The chat models the key can see, from the provider's <c>/models</c> endpoint (empty when it has none).</summary>
    public async Task<IReadOnlyList<ProviderModel>> ListModelsAsync(CopilotProvider provider, string baseUrl, string apiKey, CancellationToken cancellationToken = default)
    {
        if (provider == CopilotProvider.AzureOpenAI)
        {
            return [];
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/models");
            Authorize(request, provider, apiKey);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            return ParseModels(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return [];
        }
    }

    /// <summary>Tests the key with the chosen model, falling back to a recommended model when that one cannot be used.</summary>
    public async Task<ProviderTestReport> TestAsync(CopilotProvider provider, string baseUrl, string apiKey, string model, string? azureDeployment = null, CancellationToken cancellationToken = default)
    {
        var attempts = new List<ProbeResult>();
        var first = await ProbeModelAsync(provider, baseUrl, apiKey, model, azureDeployment, cancellationToken).ConfigureAwait(false);
        attempts.Add(first);
        if (first.Outcome == ProbeOutcome.KeyRejected)
        {
            return new ProviderTestReport(false, true, null, false, "The key was rejected: " + first.Message, [], attempts);
        }

        if (first.Outcome == ProbeOutcome.Failed)
        {
            return new ProviderTestReport(false, false, null, false, Describe(first), [], attempts);
        }

        var available = await ListModelsAsync(provider, baseUrl, apiKey, cancellationToken).ConfigureAwait(false);
        if (first.Outcome == ProbeOutcome.Works)
        {
            return new ProviderTestReport(true, false, model, false, $"The key works with {Name(provider, model)}.", available, attempts);
        }

        // The key is valid but this model cannot be used now: try the recommended ones this key offers.
        var ids = available.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = CopilotProviderDefaults.Models(provider)
            .Select(option => option.ModelId)
            .Where(id => !string.Equals(id, model, StringComparison.OrdinalIgnoreCase) && (ids.Count == 0 || ids.Contains(id)))
            .Take(MaxFallbackAttempts)
            .ToList();
        foreach (var candidate in candidates)
        {
            var result = await ProbeModelAsync(provider, baseUrl, apiKey, candidate, azureDeployment, cancellationToken).ConfigureAwait(false);
            attempts.Add(result);
            if (result.Outcome == ProbeOutcome.KeyRejected)
            {
                return new ProviderTestReport(false, true, null, false, "The key was rejected: " + result.Message, available, attempts);
            }

            if (result.Outcome == ProbeOutcome.Works)
            {
                return new ProviderTestReport(
                    true,
                    false,
                    candidate,
                    true,
                    $"The key works. {Name(provider, model)} {Reason(first)}, so Rex now uses {Name(provider, candidate)}.",
                    available,
                    attempts);
            }
        }

        return new ProviderTestReport(
            true,
            false,
            null,
            false,
            $"The key is valid, but no model answered: {Name(provider, model)} {Reason(first)}. Try again in a few minutes or pick another model.",
            available,
            attempts);
    }

    /// <summary>Reads a provider error message from an object (<c>{"error":{…}}</c>), an array (Gemini wraps it: <c>[{"error":{…}}]</c>) or plain text.</summary>
    public static string? ErrorText(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return Find(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }

        static string? Find(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        if (Find(item) is { } found)
                        {
                            return found;
                        }
                    }

                    return null;
                case JsonValueKind.Object:
                    if (element.TryGetProperty("error", out var error))
                    {
                        return error.ValueKind switch
                        {
                            JsonValueKind.String => error.GetString(),
                            JsonValueKind.Object when error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String => message.GetString(),
                            _ => error.ToString(),
                        };
                    }

                    if (element.TryGetProperty("message", out var plain) && plain.ValueKind == JsonValueKind.String)
                    {
                        return plain.GetString();
                    }

                    if (element.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                    {
                        return detail.GetString();
                    }

                    return null;
                default:
                    return null;
            }
        }
    }

    /// <summary>Sorts an error status and message into a key problem, a model problem, a capacity problem or other.</summary>
    public static ProbeOutcome Classify(int status, string message)
    {
        var text = message.ToLowerInvariant();
        if (status is 401 or 403 && !text.Contains("model", StringComparison.Ordinal)
            || text.Contains("api key not valid", StringComparison.Ordinal)
            || text.Contains("invalid api key", StringComparison.Ordinal)
            || text.Contains("incorrect api key", StringComparison.Ordinal)
            || text.Contains("invalid x-api-key", StringComparison.Ordinal)
            || text.Contains("api_key_invalid", StringComparison.Ordinal)
            || text.Contains("invalid authentication", StringComparison.Ordinal)
            || text.Contains("invalid auth", StringComparison.Ordinal)
            || text.Contains("valid api key", StringComparison.Ordinal)
            || text.Contains("api key expired", StringComparison.Ordinal)
            || text.Contains("invalid_api_key", StringComparison.Ordinal)
            || text.Contains("unauthorized", StringComparison.Ordinal) && status is 400 or 401)
        {
            return ProbeOutcome.KeyRejected;
        }

        if (status == 429 || text.Contains("quota", StringComparison.Ordinal) || text.Contains("rate limit", StringComparison.Ordinal))
        {
            return ProbeOutcome.QuotaExceeded;
        }

        if (status is 500 or 502 or 503 or 504 or 529 || text.Contains("overloaded", StringComparison.Ordinal) || text.Contains("high demand", StringComparison.Ordinal))
        {
            return ProbeOutcome.Busy;
        }

        if (status == 404
            || text.Contains("model", StringComparison.Ordinal)
               && (text.Contains("not found", StringComparison.Ordinal)
                   || text.Contains("does not exist", StringComparison.Ordinal)
                   || text.Contains("no longer available", StringComparison.Ordinal)
                   || text.Contains("not supported", StringComparison.Ordinal)
                   || text.Contains("decommissioned", StringComparison.Ordinal)
                   || text.Contains("deprecated", StringComparison.Ordinal)
                   || text.Contains("invalid model", StringComparison.Ordinal)
                   || text.Contains("not available", StringComparison.Ordinal)
                   || text.Contains("do not have access", StringComparison.Ordinal)
                   || text.Contains("does not have access", StringComparison.Ordinal)))
        {
            return ProbeOutcome.ModelUnavailable;
        }

        return ProbeOutcome.Failed;
    }

    /// <summary>Parses an OpenAI-style (<c>data</c>) or Gemini-style (<c>models</c>) list and keeps chat models.</summary>
    public static IReadOnlyList<ProviderModel> ParseModels(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        JsonElement list;
        if (root.ValueKind == JsonValueKind.Array)
        {
            list = root;
        }
        else if (!root.TryGetProperty("data", out list) && !root.TryGetProperty("models", out list))
        {
            return [];
        }

        var models = new List<ProviderModel>();
        foreach (var item in list.EnumerateArray())
        {
            var id = Text(item, "id") ?? Text(item, "name") ?? Text(item, "model");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (id.StartsWith("models/", StringComparison.Ordinal))
            {
                id = id["models/".Length..];
            }

            if (!IsChatModel(id))
            {
                continue;
            }

            var name = Text(item, "display_name") ?? Text(item, "displayName") ?? Text(item, "name") ?? id;
            if (name.StartsWith("models/", StringComparison.Ordinal))
            {
                name = id;
            }

            models.Add(new ProviderModel(id, name));
        }

        return models.DistinctBy(model => model.Id, StringComparer.OrdinalIgnoreCase).ToList();

        static string? Text(JsonElement element, string property) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>Speech, image, embedding, moderation and other non-chat models are left out of the picker.</summary>
    public static bool IsChatModel(string id)
    {
        var text = id.ToLowerInvariant();
        string[] excluded =
        [
            "embed", "tts", "whisper", "transcribe", "speech", "audio", "realtime", "image", "imagen", "dall-e", "veo", "lyria",
            "nano-banana", "moderation", "guard", "safeguard", "robotics", "computer-use", "deep-research", "aqa", "orpheus",
            "babbage", "davinci", "rerank", "ocr", "sora",
        ];
        return !excluded.Any(fragment => text.Contains(fragment, StringComparison.Ordinal));
    }

    private static void Authorize(HttpRequestMessage request, CopilotProvider provider, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        switch (provider)
        {
            case CopilotProvider.AzureOpenAI:
                request.Headers.Add("api-key", apiKey.Trim());
                break;
            case CopilotProvider.Claude:
                // Anthropic's own endpoints (the model list) authenticate with these headers.
                request.Headers.Add("x-api-key", apiKey.Trim());
                request.Headers.Add("anthropic-version", "2023-06-01");
                break;
        }
    }

    private static string Name(CopilotProvider provider, string model) =>
        CopilotProviderDefaults.Models(provider).FirstOrDefault(option => string.Equals(option.ModelId, model, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? model;

    private static string Reason(ProbeResult result) => result.Outcome switch
    {
        ProbeOutcome.ModelUnavailable => "is not available for this key",
        ProbeOutcome.Busy => "is overloaded right now",
        ProbeOutcome.QuotaExceeded => "has no quota left on this account (it may need a paid plan)",
        _ => "did not answer",
    };

    private static string Describe(ProbeResult result) =>
        result.Status == 0 ? "Could not reach the provider: " + result.Message : $"Failed ({result.Status}): {result.Message}";

    private static string Trim(string body) => body.Length > 300 ? body[..300] + "…" : body;
}
