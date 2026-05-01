using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xabbo.Configuration;
using Xabbo.Services.Abstractions;

namespace Xabbo.Services;

public sealed class OllamaService : IOllamaService
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    private readonly IConfigProvider<AppConfig> _configProvider;

    private AiConfig Config => _configProvider.Value.Ai;

    public OllamaService(IConfigProvider<AppConfig> configProvider)
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
        var endpoint = Config.OllamaEndpoint?.TrimEnd('/');
        var model = Config.Model;

        if (string.IsNullOrWhiteSpace(endpoint))
            return new AiAvailability(false, false, model, "Endpoint not configured");

        try
        {
            var res = await _http.GetAsync($"{endpoint}/api/tags", ct);
            if (!res.IsSuccessStatusCode)
                return new AiAvailability(false, false, model, $"HTTP {(int)res.StatusCode}");

            var tags = await res.Content.ReadFromJsonAsync(
                OllamaJsonContext.Default.OllamaTagsResponse, ct);

            var modelAvailable = tags?.Models?.Any(m =>
                string.Equals(m.Name, model, StringComparison.OrdinalIgnoreCase)) ?? false;

            return new AiAvailability(true, modelAvailable, model, modelAvailable ? null : "Model not pulled");
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
        var endpoint = Config.OllamaEndpoint?.TrimEnd('/');
        var model = Config.Model;

        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("Ollama endpoint is not configured.");
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Ollama model is not configured.");

        var payload = new OllamaChatRequest(
            Model: model,
            Stream: true,
            Messages: new[]
            {
                new OllamaChatMessage("system", systemPrompt),
                new OllamaChatMessage("user", userPrompt),
            },
            Options: new OllamaOptions(NumCtx: ComputeNumCtx(systemPrompt, userPrompt)));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/api/chat")
        {
            Content = JsonContent.Create(payload, OllamaJsonContext.Default.OllamaChatRequest),
        };

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            OllamaStreamChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize(line, OllamaJsonContext.Default.OllamaStreamChunk);
            }
            catch (JsonException)
            {
                continue;
            }

            if (chunk is null)
                continue;

            if (!string.IsNullOrEmpty(chunk.Message?.Content))
                yield return chunk.Message.Content;

            if (chunk.Done)
                yield break;
        }
    }

    private static int ComputeNumCtx(string systemPrompt, string userPrompt)
    {
        var estimatedInputTokens = (systemPrompt.Length + userPrompt.Length) / 3;
        var withOutputBudget = (int)(estimatedInputTokens * 1.25) + 512;

        const int step = 4096;
        const int min = 4096;
        const int max = 16384;
        var rounded = ((withOutputBudget + step - 1) / step) * step;
        return Math.Clamp(rounded, min, max);
    }
}

internal sealed record OllamaChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("stream")] bool Stream,
    [property: JsonPropertyName("messages")] IReadOnlyList<OllamaChatMessage> Messages,
    [property: JsonPropertyName("options")] OllamaOptions? Options = null);

internal sealed record OllamaOptions(
    [property: JsonPropertyName("num_ctx")] int NumCtx);

internal sealed record OllamaChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

internal sealed record OllamaStreamChunk(
    [property: JsonPropertyName("message")] OllamaChatMessage? Message,
    [property: JsonPropertyName("done")] bool Done);

internal sealed record OllamaTagsResponse(
    [property: JsonPropertyName("models")] IReadOnlyList<OllamaTagModel>? Models);

internal sealed record OllamaTagModel(
    [property: JsonPropertyName("name")] string Name);

[JsonSerializable(typeof(OllamaChatRequest))]
[JsonSerializable(typeof(OllamaStreamChunk))]
[JsonSerializable(typeof(OllamaTagsResponse))]
internal sealed partial class OllamaJsonContext : JsonSerializerContext;
