namespace Xabbo.ViewModels;

public sealed class PrivateMessageViewModel
{
    public string SenderName { get; init; } = "";
    public string Content { get; init; } = "";
    public DateTimeOffset Timestamp { get; init; }
    public bool IsFromMe { get; init; }
}
