namespace Xabbo.Services.Abstractions;

/// <summary>
/// Common interface for AI providers that can produce streaming chat summaries.
/// Implemented by both local (Ollama) and cloud (Gemini) backends.
/// </summary>
public interface IAiSummarizationProvider
{
    Task<AiAvailability> CheckAvailabilityAsync(CancellationToken ct = default);
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    IAsyncEnumerable<string> StreamChatAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}

public sealed record AiAvailability(bool Reachable, bool ModelAvailable, string? ConfiguredModel, string? ErrorMessage);
