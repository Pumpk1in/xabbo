namespace Xabbo.ViewModels;

/// <summary>
/// Represents a segment of a chat message, either normal text or flagged profanity.
/// </summary>
public sealed class MessageSegment
{
    public required string Text { get; init; }
    public bool IsProfanity { get; init; }
}
