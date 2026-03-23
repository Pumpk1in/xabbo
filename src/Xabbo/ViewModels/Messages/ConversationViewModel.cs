using System.Collections.ObjectModel;
using ReactiveUI;
using Xabbo.Core;

namespace Xabbo.ViewModels;

public sealed class ConversationViewModel : ReactiveObject
{
    public const int MaxMessages = 150;

    public Id FriendId { get; init; }

    [Reactive] public string FriendName { get; set; } = "";
    [Reactive] public string FriendFigure { get; set; } = "";
    [Reactive] public bool IsOnline { get; set; }
    [Reactive] public int UnreadCount { get; set; }
    [Reactive] public DateTimeOffset LastMessageTime { get; set; }

    public ObservableCollection<PrivateMessageViewModel> Messages { get; } = [];

    public PrivateMessageViewModel? LastMessage => Messages.Count > 0 ? Messages[^1] : null;

    /// <summary>
    /// True while history is being loaded from DB. New incoming messages are held in PendingMessages.
    /// </summary>
    public bool IsLoadingHistory { get; set; }

    /// <summary>
    /// True once conversation history has been fully loaded from DB.
    /// </summary>
    public bool IsHistoryLoaded { get; set; }

    public List<PrivateMessageViewModel> PendingMessages { get; } = [];

    public void AddMessage(PrivateMessageViewModel msg)
    {
        Messages.Add(msg);
        while (Messages.Count > MaxMessages)
            Messages.RemoveAt(0);
        this.RaisePropertyChanged(nameof(LastMessage));
    }
}
