using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xabbo.Configuration;
using Xabbo.Services.Abstractions;

namespace Xabbo.Services;

public sealed class GeminiService : IGeminiService
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    private readonly IConfigProvider<AppConfig> _configProvider;

    private AiConfig Config => _configProvider.Value.Ai;

    public GeminiService(IConfigProvider<AppConfig> configProvider)
    {
        _configProvider = configProvider;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        var status = await CheckAvailabilityAsync(ct);
        return status.Reachable && status.ModelAvailable;
    }

    public async Task<AiAvailability> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        var apiKey = Config.GeminiApiKey;
        var model = Config.GeminiModel;

        if (string.IsNullOrWhiteSpace(apiKey))
            return new AiAvailability(false, false, model, "API key not configured");
        if (string.IsNullOrWhiteSpace(model))
            return new AiAvailability(false, false, model, "Model not configured");

        try
        {
            var url = $"{BaseUrl}/models?key={Uri.EscapeDataString(apiKey)}";
            var res = await _http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct);
                return new AiAvailability(false, false, model, $"HTTP {(int)res.StatusCode}: {Truncate(body, 200)}");
            }

            var list = await res.Content.ReadFromJsonAsync(GeminiJsonContext.Default.GeminiModelsResponse, ct);

            // Gemini model names are returned as "models/gemini-2.0-flash" or "models/gemini-1.5-flash-001".
            // Match if the API name contains the configured model id (handles version suffixes like -001, -latest).
            var modelAvailable = list?.Models?.Any(m =>
            {
                if (string.IsNullOrEmpty(m.Name)) return false;
                var apiName = m.Name.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                    ? m.Name.Substring("models/".Length)
                    : m.Name;
                return apiName.Equals(model, StringComparison.OrdinalIgnoreCase)
                    || apiName.StartsWith(model + "-", StringComparison.OrdinalIgnoreCase)
                    || model.StartsWith(apiName + "-", StringComparison.OrdinalIgnoreCase);
            }) ?? false;

            return new AiAvailability(true, modelAvailable, model, modelAvailable ? null : "Model not available for this API key");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AiAvailability(false, false, model, ex.Message);
        }
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        string systemPrompt,
        string userPrompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var apiKey = Config.GeminiApiKey;
        var model = Config.GeminiModel;

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Gemini API key is not configured.");
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Gemini model is not configured.");

        var payload = new GeminiGenerateRequest(
            SystemInstruction: string.IsNullOrEmpty(systemPrompt)
                ? null
                : new GeminiContent(null, new[] { new GeminiPart(systemPrompt) }),
            Contents: new[]
            {
                new GeminiContent("user", new[] { new GeminiPart(userPrompt) }),
            },
            SafetySettings: new[]
            {
                new GeminiSafetySetting("HARM_CATEGORY_HARASSMENT", "BLOCK_NONE"),
                new GeminiSafetySetting("HARM_CATEGORY_HATE_SPEECH", "BLOCK_NONE"),
                new GeminiSafetySetting("HARM_CATEGORY_SEXUALLY_EXPLICIT", "BLOCK_NONE"),
                new GeminiSafetySetting("HARM_CATEGORY_DANGEROUS_CONTENT", "BLOCK_NONE"),
            });

        var url = $"{BaseUrl}/models/{Uri.EscapeDataString(model)}:streamGenerateContent?alt=sse&key={Uri.EscapeDataString(apiKey)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload, GeminiJsonContext.Default.GeminiGenerateRequest),
        };

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Gemini API error HTTP {(int)response.StatusCode}: {Truncate(body, 500)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line))
                continue;

            // SSE format: "data: {...json...}"
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var jsonPart = line.Substring(5).TrimStart();
            if (string.IsNullOrEmpty(jsonPart) || jsonPart == "[DONE]")
                continue;

            GeminiStreamChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize(jsonPart, GeminiJsonContext.Default.GeminiStreamChunk);
            }
            catch (JsonException)
            {
                continue;
            }

            if (chunk?.Candidates is null)
                continue;

            foreach (var candidate in chunk.Candidates)
            {
                if (candidate.Content?.Parts is null)
                    continue;

                foreach (var part in candidate.Content.Parts)
                {
                    if (!string.IsNullOrEmpty(part.Text))
                        yield return part.Text;
                }
            }
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max) + "…";
}

internal sealed record GeminiGenerateRequest(
    [property: JsonPropertyName("system_instruction")] GeminiContent? SystemInstruction,
    [property: JsonPropertyName("contents")] IReadOnlyList<GeminiContent> Contents,
    [property: JsonPropertyName("safetySettings")] IReadOnlyList<GeminiSafetySetting>? SafetySettings = null);

internal sealed record GeminiContent(
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("parts")] IReadOnlyList<GeminiPart> Parts);

internal sealed record GeminiPart(
    [property: JsonPropertyName("text")] string Text);

internal sealed record GeminiSafetySetting(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("threshold")] string Threshold);

internal sealed record GeminiStreamChunk(
    [property: JsonPropertyName("candidates")] IReadOnlyList<GeminiCandidate>? Candidates);

internal sealed record GeminiCandidate(
    [property: JsonPropertyName("content")] GeminiContent? Content,
    [property: JsonPropertyName("finishReason")] string? FinishReason);

internal sealed record GeminiModelsResponse(
    [property: JsonPropertyName("models")] IReadOnlyList<GeminiModelInfo>? Models);

internal sealed record GeminiModelInfo(
    [property: JsonPropertyName("name")] string? Name);

[JsonSerializable(typeof(GeminiGenerateRequest))]
[JsonSerializable(typeof(GeminiStreamChunk))]
[JsonSerializable(typeof(GeminiModelsResponse))]
internal sealed partial class GeminiJsonContext : JsonSerializerContext;
