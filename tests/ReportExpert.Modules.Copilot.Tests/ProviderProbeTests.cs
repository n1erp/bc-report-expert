using System.Net;
using System.Net.Http;
using System.Text;
using ReportExpert.Modules.Copilot.Models;
using ReportExpert.Modules.Copilot.Services;

namespace ReportExpert.Modules.Copilot.Tests;

/// <summary>Settings › Test: key checks, provider error bodies and the fallback to a model that works.</summary>
public sealed class ProviderProbeTests
{
    private const string GeminiBase = "https://generativelanguage.googleapis.com/v1beta/openai";

    [Fact]
    public void ErrorText_reads_gemini_array_bodies()
    {
        const string body = """[{"error":{"code":404,"message":"This model models/gemini-2.5-flash is no longer available to new users.","status":"NOT_FOUND"}}]""";
        Assert.Equal("This model models/gemini-2.5-flash is no longer available to new users.", ProviderProbe.ErrorText(body));
    }

    [Theory]
    [InlineData("""{"error":{"message":"Invalid API Key","type":"invalid_request_error"}}""", "Invalid API Key")]
    [InlineData("""{"error":"model not found"}""", "model not found")]
    [InlineData("""{"message":"Unauthorized"}""", "Unauthorized")]
    [InlineData("""{"detail":"Rate limited"}""", "Rate limited")]
    public void ErrorText_reads_object_bodies(string body, string expected) =>
        Assert.Equal(expected, ProviderProbe.ErrorText(body));

    [Fact]
    public void ErrorText_does_not_throw_on_plain_text() =>
        Assert.Null(Record.Exception(() => ProviderProbe.ErrorText("<html>Bad gateway</html>")));

    [Theory]
    [InlineData(401, "Invalid API Key", ProbeOutcome.KeyRejected)]
    [InlineData(400, "API key not valid. Please pass a valid API key.", ProbeOutcome.KeyRejected)]
    [InlineData(400, "Invalid Auth key.", ProbeOutcome.KeyRejected)]
    [InlineData(400, "Please pass a valid API key", ProbeOutcome.KeyRejected)]
    [InlineData(400, "API key expired. Please renew the API key.", ProbeOutcome.KeyRejected)]
    [InlineData(404, "This model is no longer available to new users.", ProbeOutcome.ModelUnavailable)]
    [InlineData(429, "You exceeded your current quota.", ProbeOutcome.QuotaExceeded)]
    [InlineData(503, "This model is currently experiencing high demand.", ProbeOutcome.Busy)]
    public void Classify_separates_bad_keys_from_model_problems(int status, string message, ProbeOutcome expected) =>
        Assert.Equal(expected, ProviderProbe.Classify(status, message));

    [Fact]
    public void ParseModels_strips_prefixes_and_keeps_chat_models()
    {
        const string body = """{"object":"list","data":[{"id":"models/gemini-3.5-flash"},{"id":"models/text-embedding-004"},{"id":"models/imagen-4.0-generate"},{"id":"models/gemini-3.8-flash"}]}""";
        var ids = ProviderProbe.ParseModels(body).Select(m => m.Id).ToList();
        Assert.Contains("gemini-3.5-flash", ids);
        Assert.Contains("gemini-3.8-flash", ids);
        Assert.DoesNotContain("text-embedding-004", ids);
        Assert.DoesNotContain(ids, id => id.StartsWith("imagen", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Retired_model_switches_to_a_recommended_one_the_key_offers()
    {
        var handler = new FakeHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
                return Json(200, """{"data":[{"id":"models/gemini-3.5-flash"},{"id":"models/gemini-3.8-flash"}]}""");
            var body = request.Content!.ReadAsStringAsync().Result;
            if (body.Contains("gemini-2.5-flash", StringComparison.Ordinal))
                return Json(404, """[{"error":{"code":404,"message":"This model models/gemini-2.5-flash is no longer available to new users.","status":"NOT_FOUND"}}]""");
            if (body.Contains("gemini-3.5-flash", StringComparison.Ordinal))
                return Json(200, """{"choices":[{"message":{"role":"assistant","content":"OK"}}]}""");
            return Json(503, """[{"error":{"code":503,"message":"high demand","status":"UNAVAILABLE"}}]""");
        });

        var report = await new ProviderProbe(new HttpClient(handler)).TestAsync(CopilotProvider.Gemini, GeminiBase, "test-key", "gemini-2.5-flash");

        Assert.True(report.KeyValid);
        Assert.False(report.KeyRejected);
        Assert.True(report.ModelChanged);
        Assert.Equal("gemini-3.5-flash", report.WorkingModel);
        Assert.Contains("Rex now uses", report.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejected_key_is_reported_without_fallback()
    {
        var calls = 0;
        var handler = new FakeHandler(_ =>
        {
            calls++;
            return Json(400, """[{"error":{"code":400,"message":"API key not valid. Please pass a valid API key.","status":"INVALID_ARGUMENT"}}]""");
        });

        var report = await new ProviderProbe(new HttpClient(handler)).TestAsync(CopilotProvider.Gemini, GeminiBase, "bad", "gemini-3.5-flash");

        Assert.False(report.KeyValid);
        Assert.True(report.KeyRejected);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Working_model_is_kept()
    {
        var handler = new FakeHandler(request => request.Method == HttpMethod.Get
            ? Json(200, """{"data":[{"id":"openai/gpt-oss-120b"}]}""")
            : Json(200, """{"choices":[{"message":{"content":"OK"}}]}"""));

        var report = await new ProviderProbe(new HttpClient(handler)).TestAsync(CopilotProvider.Groq, "https://api.groq.com/openai/v1", "k", "openai/gpt-oss-120b");

        Assert.True(report.KeyValid);
        Assert.False(report.ModelChanged);
        Assert.Equal("openai/gpt-oss-120b", report.WorkingModel);
        Assert.Single(report.AvailableModels);
    }

    [Fact]
    public async Task Claude_requests_carry_anthropic_headers()
    {
        HttpRequestMessage? seen = null;
        var handler = new FakeHandler(request =>
        {
            seen ??= request;
            return Json(200, """{"choices":[{"message":{"content":"OK"}}]}""");
        });

        await new ProviderProbe(new HttpClient(handler)).ProbeModelAsync(CopilotProvider.Claude, "https://api.anthropic.com/v1", "k", "claude-sonnet-5-5");

        Assert.NotNull(seen);
        Assert.True(seen!.Headers.Contains("x-api-key"));
        Assert.True(seen.Headers.Contains("anthropic-version"));
    }

    [Fact]
    public void Every_provider_offers_a_default_model()
    {
        foreach (var provider in Enum.GetValues<CopilotProvider>())
        {
            var models = CopilotProviderDefaults.Models(provider);
            Assert.NotEmpty(models);
            Assert.Equal(models[0].ModelId, CopilotProviderDefaults.DefaultModel(provider));
            Assert.Equal(models.Count, models.Select(m => m.ModelId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    private static HttpResponseMessage Json(int status, string body) =>
        new((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
