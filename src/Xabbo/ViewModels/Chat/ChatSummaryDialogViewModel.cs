using System.Reactive;
using System.Reactive.Linq;
using System.Text;

using HanumanInstitute.MvvmDialogs;
using ReactiveUI;

using Xabbo.Models;
using Xabbo.Services;
using Xabbo.Services.Abstractions;

namespace Xabbo.ViewModels;

public class ChatSummaryDialogViewModel : ViewModelBase, IModalDialogViewModel, IDisposable
{
    public bool? DialogResult => null;

    private readonly IAiSummarizationProvider _ollama;
    private readonly IClipboardService _clipboard;
    private readonly IUiContext _ui;

    private IReadOnlyList<ChatHistoryEntry>? _messages;
    private string _systemPrompt = "";
    private int _mapReduceThreshold = int.MaxValue;
    private CancellationTokenSource? _cts;

    [Reactive] public string SummaryText { get; set; } = "";
    [Reactive] public bool IsStreaming { get; set; }
    [Reactive] public string? ErrorMessage { get; set; }
    [Reactive] public string? WarningMessage { get; set; }
    [Reactive] public int MessageCount { get; set; }
    [Reactive] public string? ModelName { get; set; }

    public ReactiveCommand<Unit, Unit> CancelCmd { get; }
    public ReactiveCommand<Unit, Unit> CopyCmd { get; }
    public ReactiveCommand<Unit, Unit> RegenerateCmd { get; }

    public ChatSummaryDialogViewModel(IAiSummarizationProvider ollama, IClipboardService clipboard, IUiContext ui)
    {
        _ollama = ollama;
        _clipboard = clipboard;
        _ui = ui;

        var canCancel = this.WhenAnyValue(x => x.IsStreaming);
        var canRunWhenIdle = this.WhenAnyValue(x => x.IsStreaming, streaming => !streaming);
        var canCopy = this.WhenAnyValue(x => x.SummaryText, x => x.IsStreaming,
            (text, streaming) => !streaming && !string.IsNullOrEmpty(text));

        CancelCmd = ReactiveCommand.Create(Cancel, canCancel);
        CopyCmd = ReactiveCommand.Create(Copy, canCopy);
        RegenerateCmd = ReactiveCommand.CreateFromTask(RegenerateAsync, canRunWhenIdle);
    }

    public Task StartAsync(IEnumerable<ChatHistoryEntry> messages, string systemPrompt, int mapReduceThreshold = int.MaxValue)
    {
        _messages = messages.ToList();
        _systemPrompt = systemPrompt;
        _mapReduceThreshold = mapReduceThreshold;
        MessageCount = _messages.Count;
        return RunStreamingAsync();
    }

    private Task RegenerateAsync()
    {
        if (_messages is null)
            return Task.CompletedTask;
        return RunStreamingAsync();
    }

    private const int MapBatchSize = 100;

    private async Task RunStreamingAsync()
    {
        if (_messages is null || _messages.Count == 0)
            return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        SummaryText = "";
        ErrorMessage = null;
        IsStreaming = true;

        var messages = _messages;
        var systemPrompt = _systemPrompt ?? "";

        try
        {
            if (messages.Count <= _mapReduceThreshold)
            {
                await StreamSingleAsync(messages, systemPrompt, ct).ConfigureAwait(false);
            }
            else
            {
                await RunMapReduceAsync(messages, systemPrompt, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on cancel
        }
        catch (Exception ex)
        {
            await _ui.InvokeAsync(() => ErrorMessage = ex.Message);
        }
        finally
        {
            await _ui.InvokeAsync(() => IsStreaming = false);
        }
    }

    private async Task StreamSingleAsync(IReadOnlyList<ChatHistoryEntry> messages, string systemPrompt, CancellationToken ct)
    {
        var userPrompt = await Task.Run(() => SummaryPromptBuilder.BuildUserPrompt(messages, systemPrompt), ct)
            .ConfigureAwait(false);

        await StreamToSummaryAsync(systemPrompt, userPrompt, "", ct).ConfigureAwait(false);
    }

    private async Task RunMapReduceAsync(IReadOnlyList<ChatHistoryEntry> messages, string systemPrompt, CancellationToken ct)
    {
        var batches = new List<IReadOnlyList<ChatHistoryEntry>>();
        for (int i = 0; i < messages.Count; i += MapBatchSize)
            batches.Add(messages.Skip(i).Take(MapBatchSize).ToList());

        var partials = new List<string>(batches.Count);

        const string mapInstruction = """
            You are extracting raw notes from a chunk of a longer chat log. List every distinct theme, conversation, joke, transaction, or conflict you can find in this chunk.

            Be exhaustive — better too many notes than too few. Produce a simple bullet list (no markdown headers, no introduction, no conclusion).

            CRITICAL ACCURACY RULES:
            - Cite usernames EXACTLY as written, character-by-character (case, dots, hyphens, special characters, accents). Do NOT normalize, fix, or "clean up" weird-looking usernames. If a user is named ".HALLUCINATlONS" (with a lowercase l), copy it exactly that way.
            - Quote insults, threats, and key phrases VERBATIM between quotes. Do not paraphrase aggressive content.
            - For each note, you must be able to point to specific messages. If you cannot, omit the note. Never invent participants or events.
            - Do not merge two distinct dramas/conversations into one note. If user A insults user B, and user C threatens user D, those are TWO separate notes.

            Write in the dominant language of the messages.
            """;

        for (int i = 0; i < batches.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var batch = batches[i];
            var batchNum = i + 1;
            var totalBatches = batches.Count;

            await _ui.InvokeAsync(() => SummaryText = $"Analyse en cours... lot {batchNum}/{totalBatches} ({batch.Count} messages)\n");

            var batchUserPrompt = await Task.Run(() => SummaryPromptBuilder.BuildUserPrompt(batch, mapInstruction), ct)
                .ConfigureAwait(false);

            var partial = new StringBuilder();
            await foreach (var chunk in _ollama.StreamChatAsync(mapInstruction, batchUserPrompt, ct).ConfigureAwait(false))
            {
                if (ct.IsCancellationRequested)
                    break;
                partial.Append(chunk);
            }

            partials.Add(partial.ToString());
        }

        ct.ThrowIfCancellationRequested();
        await _ui.InvokeAsync(() => SummaryText = "Consolidation finale...\n");

        var reducePrompt = new StringBuilder();
        reducePrompt.AppendLine("=== RAW NOTES FROM EACH CHUNK ===");
        reducePrompt.AppendLine();
        for (int i = 0; i < partials.Count; i++)
        {
            reducePrompt.AppendLine($"--- Chunk {i + 1}/{partials.Count} ---");
            reducePrompt.AppendLine(partials[i]);
            reducePrompt.AppendLine();
        }
        reducePrompt.AppendLine("=== END RAW NOTES ===");
        reducePrompt.AppendLine();
        reducePrompt.AppendLine("=== INSTRUCTIONS REMINDER (apply these strictly to consolidate the notes above) ===");
        reducePrompt.AppendLine(systemPrompt);
        reducePrompt.AppendLine("=== END REMINDER ===");

        await StreamToSummaryAsync(systemPrompt, reducePrompt.ToString(), "", ct).ConfigureAwait(false);
    }

    private async Task StreamToSummaryAsync(string systemPrompt, string userPrompt, string initialText, CancellationToken ct)
    {
        var buffer = new StringBuilder(initialText);
        await _ui.InvokeAsync(() => SummaryText = buffer.ToString());
        var lastFlush = Environment.TickCount64;

        await foreach (var chunk in _ollama.StreamChatAsync(systemPrompt, userPrompt, ct).ConfigureAwait(false))
        {
            if (ct.IsCancellationRequested)
                break;

            buffer.Append(chunk);

            var now = Environment.TickCount64;
            if (now - lastFlush >= 50)
            {
                var snapshot = buffer.ToString();
                await _ui.InvokeAsync(() => SummaryText = snapshot);
                lastFlush = now;
            }
        }

        var finalText = buffer.ToString();
        await _ui.InvokeAsync(() => SummaryText = finalText);
    }

    private void Cancel()
    {
        _cts?.Cancel();
    }

    private void Copy()
    {
        if (!string.IsNullOrEmpty(SummaryText))
            _clipboard.SetText(SummaryText);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
