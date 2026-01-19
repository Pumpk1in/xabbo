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

    /// <summary>
    /// Whether this message was sent by the current user.
    /// </summary>
    public bool IsOwnMessage { get; init; }

    /// <summary>
    /// Whether to show moderation buttons (has profanity and not own message).
    /// </summary>
    public bool ShowModerationButtons => HasProfanity && !IsOwnMessage;

    // Quick action parameters (Name, Duration/BanType)
    public (string Name, int Minutes) MuteParam2 => (Name, 2);
    public (string Name, int Minutes) MuteParam5 => (Name, 5);
    public (string Name, int Minutes) MuteParam10 => (Name, 10);
    public (string Name, int Minutes) MuteParam15 => (Name, 15);
    public (string Name, int Minutes) MuteParam30 => (Name, 30);
    public (string Name, int Minutes) MuteParam60 => (Name, 60);

    public (string Name, BanDuration Duration) BanParamHour => (Name, BanDuration.Hour);
    public (string Name, BanDuration Duration) BanParamDay => (Name, BanDuration.Day);
    public (string Name, BanDuration Duration) BanParamPermanent => (Name, BanDuration.Permanent);

    public override string ToString() => $"[{Timestamp:HH:mm:ss}] {Name}: {H.RenderText(Message)}";
}