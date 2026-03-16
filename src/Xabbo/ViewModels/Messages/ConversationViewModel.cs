using System.Collections.ObjectModel;
using ReactiveUI;
using Xabbo.Core;

namespace Xabbo.ViewModels;

public sealed class ConversationViewModel : ReactiveObject
{
    public Id FriendId { get; init; }

    [Reactive] public string FriendName { get; set; } = "";
    [Reactive] public string FriendFigure { get; set; } = "";
    [Reactive] public int UnreadCount { get; set; }
    [Reactive] public DateTimeOffset LastMessageTime { get; set; }

    public ObservableCollection<PrivateMessageViewModel> Messages { get; } = [];

    public PrivateMessageViewModel? LastMessage => Messages.Count > 0 ? Messages[^1] : null;

    /// <summary>
    /// True while history is being loaded from DB. New incoming messages are held in PendingMessages.
    /// </summary>
    public bool IsLoadingHistory { get; set; }
    public List<PrivateMessageViewModel> PendingMessages { get; } = [];
}
