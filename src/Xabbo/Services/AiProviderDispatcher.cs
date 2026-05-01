using System.Runtime.CompilerServices;

using Xabbo.Configuration;
using Xabbo.Services.Abstractions;

namespace Xabbo.Services;

/// <summary>
/// Routes summarization requests to the active provider (Ollama or Gemini)
/// based on the user's <see cref="AiConfig.Provider"/> setting.
/// </summary>
public sealed class AiProviderDispatcher : IAiSummarizationProvider
{
    private readonly IConfigProvider<AppConfig> _configProvider;
    private readonly IOllamaService _ollama;
    private readonly IGeminiService _gemini;

    public AiProviderDispatcher(
        IConfigProvider<AppConfig> configProvider,
        IOllamaService ollama,
        IGeminiService gemini)
    {
        _configProvider = configProvider;
        _ollama = ollama;
        _gemini = gemini;
    }

    private IAiSummarizationProvider Active =>
        _configProvider.Value.Ai.Provider switch
        {
            AiProvider.Gemini => _gemini,
            _ => _ollama,
        };

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) =>
        Active.IsAvailableAsync(ct);

    public Task<AiAvailability> CheckAvailabilityAsync(CancellationToken ct = default) =>
        Active.CheckAvailabilityAsync(ct);

    public IAsyncEnumerable<string> StreamChatAsync(string systemPrompt, string userPrompt, CancellationToken ct = default) =>
        Active.StreamChatAsync(systemPrompt, userPrompt, ct);
}
