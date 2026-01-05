using Xabbo.Core;

namespace Xabbo.ViewModels;

public class ChatMessageViewModel : ChatLogEntryViewModel
{
    public string? FigureString { get; set; }
    public required ChatType Type { get; init; }
    public required string Name { get; init; }
    public required string Message { get; init; }
    public required int BubbleStyle { get; init; }

    /// <summary>
    /// Whether this message contains profanity.
    /// </summary>
    public bool HasProfanity { get; init; }

    /// <summary>
    /// Message segments for displaying with profanity highlighting.
    /// If null or empty, display the Message as-is.
    /// </summary>
    public IReadOnlyList<MessageSegment>? MessageSegments { get; init; }

    /// <summary>
    /// List of matched profanity words (base words from the filter).
    /// </summary>
    public IReadOnlyList<string>? MatchedWords { get; init; }

    public bool IsTalk => Type is ChatType.Talk;
    public bool IsShout => Type is ChatType.Shout;
    public bool IsWhisper => Type is ChatType.Whisper;

    public override string ToString() => $"[{Timestamp:HH:mm:ss}] {Name}: {H.RenderText(Message)}";
}