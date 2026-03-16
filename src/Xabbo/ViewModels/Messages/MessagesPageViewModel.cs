using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using FluentAvalonia.UI.Controls;
using SymbolIconSource = FluentIcons.Avalonia.Fluent.SymbolIconSource;
using Symbol = FluentIcons.Common.Symbol;

using Xabbo.Core;
using Xabbo.Core.Game;
using Xabbo.Core.Events;
using Xabbo.Core.Messages.Outgoing;
using Xabbo.Interceptor;
using Xabbo.Messages;
using Xabbo.Services.Abstractions;
using Xabbo.Services;

namespace Xabbo.ViewModels;

public sealed class MessagesPageViewModel : PageViewModel
{
    public override string Header => TotalUnread > 0 ? $"Messages ({TotalUnread})" : "Messages";
    public override int BadgeCount => TotalUnread;
    public override IconSource? Icon { get; } = new SymbolIconSource { Symbol = Symbol.ChatMultiple };

    private readonly IUiContext _uiContext;
    private readonly IInterceptor _interceptor;
    private readonly FriendManager _friendManager;
    private readonly ProfileManager _profileManager;
    private readonly IPrivateMessageHistoryService _history;

    private readonly SourceCache<ConversationViewModel, Id> _cache = new(x => x.FriendId);

    private readonly ReadOnlyObservableCollection<ConversationViewModel> _conversations;
    public ReadOnlyObservableCollection<ConversationViewModel> Conversations => _conversations;

    [Reactive] public ConversationViewModel? SelectedConversation { get; set; }
    [Reactive] public string ReplyText { get; set; } = "";
    [Reactive] public int TotalUnread { get; private set; }
    [Reactive] public bool IsActive { get; set; }

    private bool _isSendingFromHere;

    public ReactiveCommand<Unit, Unit> SendReplyCmd { get; }
    public ReactiveCommand<ConversationViewModel, Unit> HideConversationCmd { get; }
    public ReactiveCommand<ConversationViewModel, Unit> DeleteConversationCmd { get; }

    public MessagesPageViewModel(
        IUiContext uiContext,
        IInterceptor interceptor,
        FriendManager friendManager,
        ProfileManager profileManager,
        IPrivateMessageHistoryService history)
    {
        _uiContext = uiContext;
        _interceptor = interceptor;
        _friendManager = friendManager;
        _profileManager = profileManager;
        _history = history;

        _cache
            .Connect()
            .ObserveOn(RxApp.MainThreadScheduler)
            .SortAndBind(
                out _conversations,
                SortExpressionComparer<ConversationViewModel>.Descending(x => x.LastMessageTime))
            .Subscribe();

        var canSend = this.WhenAnyValue(
            x => x.SelectedConversation,
            x => x.ReplyText,
            (conv, text) => conv is not null && !string.IsNullOrWhiteSpace(text));

        SendReplyCmd = ReactiveCommand.Create(SendReply, canSend);
        HideConversationCmd = ReactiveCommand.Create<ConversationViewModel>(HideConversation);
        DeleteConversationCmd = ReactiveCommand.CreateFromTask<ConversationViewModel>(DeleteConversationAsync);

        this.WhenAnyValue(x => x.TotalUnread)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(Header));
                this.RaisePropertyChanged(nameof(BadgeCount));
            });

        this.WhenAnyValue(x => x.SelectedConversation)
            .Subscribe(conv =>
            {
                if (conv is null || !IsActive) return;
                TotalUnread -= conv.UnreadCount;
                if (TotalUnread < 0) TotalUnread = 0;
                conv.UnreadCount = 0;
            });

        this.WhenAnyValue(x => x.IsActive)
            .Subscribe(active =>
            {
                if (!active || SelectedConversation is null) return;
                TotalUnread -= SelectedConversation.UnreadCount;
                if (TotalUnread < 0) TotalUnread = 0;
                SelectedConversation.UnreadCount = 0;
            });

        _friendManager.Loaded += OnFriendsLoaded;
        _friendManager.MessageReceived += OnMessageReceived;
        _interceptor.Intercept<SendConsoleMessageMsg>(OnSentConsoleMessage);

        _ = LoadHistoryAsync();
    }

    private void OnFriendsLoaded()
    {
        // Met à jour les figures des conversations déjà créées depuis l'historique
        _uiContext.Invoke(() =>
        {
            foreach (var conv in _cache.Items)
            {
                if (!string.IsNullOrEmpty(conv.FriendFigure)) continue;
                var figure = _friendManager.GetFriend(conv.FriendId)?.Figure;
                if (!string.IsNullOrEmpty(figure))
                    conv.FriendFigure = figure;
            }
        });
    }

    private async Task LoadHistoryAsync()
    {
        var records = await _history.LoadAllAsync();
        _uiContext.Invoke(() =>
        {
            foreach (var r in records)
            {
                // Crée ou récupère la conversation sans déclencher LoadConversationHistoryAsync
                var lookup = _cache.Lookup(r.FriendId);
                ConversationViewModel conv;
                if (lookup.HasValue)
                {
                    conv = lookup.Value;
                }
                else
                {
                    conv = new ConversationViewModel
                    {
                        FriendId = r.FriendId,
                        FriendName = r.FriendName,
                        FriendFigure = _friendManager.GetFriend(r.FriendId)?.Figure ?? "",
                    };
                    _cache.AddOrUpdate(conv);
                }
                conv.Messages.Add(new PrivateMessageViewModel
                {
                    SenderName = r.SenderName,
                    Content = r.Content,
                    Timestamp = r.Timestamp,
                    IsFromMe = r.IsFromMe,
                });
                if (r.Timestamp > conv.LastMessageTime)
                    conv.LastMessageTime = r.Timestamp;
            }
        });
    }

    private ConversationViewModel GetOrCreateConversationById(Id friendId, string friendName)
    {
        var lookup = _cache.Lookup(friendId);
        if (lookup.HasValue) return lookup.Value;

        var vm = new ConversationViewModel
        {
            FriendId = friendId,
            FriendName = friendName,
            FriendFigure = _friendManager.GetFriend(friendId)?.Figure ?? "",
            IsLoadingHistory = true,
        };
        _cache.AddOrUpdate(vm);
        _ = LoadConversationHistoryAsync(vm);
        return vm;
    }

    private async Task LoadConversationHistoryAsync(ConversationViewModel conv)
    {
        var records = await _history.LoadConversationAsync(conv.FriendId);
        _uiContext.Invoke(() =>
        {
            if (!_cache.Lookup(conv.FriendId).HasValue) return;

            foreach (var r in records)
            {
                conv.Messages.Add(new PrivateMessageViewModel
                {
                    SenderName = r.SenderName,
                    Content = r.Content,
                    Timestamp = r.Timestamp,
                    IsFromMe = r.IsFromMe,
                });
                if (r.Timestamp > conv.LastMessageTime)
                    conv.LastMessageTime = r.Timestamp;
            }

            // Flush les messages arrivés pendant le chargement — on les enqueue maintenant en DB
            conv.IsLoadingHistory = false;
            foreach (var m in conv.PendingMessages)
            {
                _history.Enqueue(conv.FriendId, conv.FriendName, m.SenderName, m.Content, m.Timestamp, m.IsFromMe);
                conv.Messages.Add(m);
            }
            conv.PendingMessages.Clear();
        });
    }

    private void OnMessageReceived(FriendMessageEventArgs e)
    {
        var timestamp = DateTimeOffset.Now;

        _uiContext.Invoke(() =>
        {
            var vm = GetOrCreateConversation(e.Friend);
            var msg = new PrivateMessageViewModel
            {
                SenderName = e.Friend.Name,
                Content = e.Message.Content,
                Timestamp = timestamp,
                IsFromMe = false,
            };

            if (vm.IsLoadingHistory)
            {
                vm.PendingMessages.Add(msg);
                // L'enqueue DB se fera au flush des pending, après le chargement
            }
            else
            {
                _history.Enqueue(e.Friend.Id, e.Friend.Name, e.Friend.Name, e.Message.Content, timestamp, isFromMe: false);
                vm.Messages.Add(msg);
            }

            vm.LastMessageTime = timestamp;

            if (!IsActive || !ReferenceEquals(SelectedConversation, vm))
            {
                vm.UnreadCount++;
                TotalUnread++;
            }
        });
    }

    private void OnSentConsoleMessage(SendConsoleMessageMsg msg)
    {
        if (_isSendingFromHere) return;

        var recipientId = msg.Recipients.Count > 0 ? msg.Recipients[0] : default;
        if (recipientId == default) return;

        var friend = _friendManager.GetFriend(recipientId);
        if (friend is null) return;

        var myName = _profileManager.UserData?.Name ?? "Me";
        var timestamp = DateTimeOffset.Now;

        _uiContext.Invoke(() =>
        {
            var conv = GetOrCreateConversation(friend);
            var pm = new PrivateMessageViewModel
            {
                SenderName = myName,
                Content = msg.Message,
                Timestamp = timestamp,
                IsFromMe = true,
            };
            if (conv.IsLoadingHistory)
            {
                conv.PendingMessages.Add(pm);
            }
            else
            {
                _history.Enqueue(friend.Id, friend.Name, myName, msg.Message, timestamp, isFromMe: true);
                conv.Messages.Add(pm);
            }
            conv.LastMessageTime = timestamp;

            // Replied from game = conversation is read
            TotalUnread -= conv.UnreadCount;
            if (TotalUnread < 0) TotalUnread = 0;
            conv.UnreadCount = 0;
        });
    }

    private ConversationViewModel GetOrCreateConversation(IFriend friend)
    {
        var conv = GetOrCreateConversationById(friend.Id, friend.Name);
        if (string.IsNullOrEmpty(conv.FriendFigure) && !string.IsNullOrEmpty(friend.Figure))
            conv.FriendFigure = friend.Figure;
        return conv;
    }

    /// <summary>
    /// Sélectionne (ou crée) la conversation avec cet ami et focus le champ de réponse.
    /// Doit être appelé sur le thread UI.
    /// </summary>
    public void OpenConversation(Id friendId, string friendName, string friendFigure)
    {
        var conv = GetOrCreateConversationById(friendId, friendName);
        if (string.IsNullOrEmpty(conv.FriendFigure) && !string.IsNullOrEmpty(friendFigure))
            conv.FriendFigure = friendFigure;
        SelectedConversation = conv;
    }

    private void HideConversation(ConversationViewModel conv)
    {
        if (ReferenceEquals(SelectedConversation, conv))
            SelectedConversation = null;
        TotalUnread -= conv.UnreadCount;
        if (TotalUnread < 0) TotalUnread = 0;
        _cache.RemoveKey(conv.FriendId);
    }

    private async Task DeleteConversationAsync(ConversationViewModel conv)
    {
        HideConversation(conv);
        await _history.DeleteConversationAsync(conv.FriendId);
    }

    private void SendReply()
    {
        var conv = SelectedConversation;
        var text = ReplyText.Trim();
        if (conv is null || string.IsNullOrEmpty(text)) return;

        var myName = _profileManager.UserData?.Name ?? "Me";
        var timestamp = DateTimeOffset.Now;

        _isSendingFromHere = true;
        try
        {
            _friendManager.SendMessage(conv.FriendId, text);
        }
        finally
        {
            _isSendingFromHere = false;
        }

        _history.Enqueue(conv.FriendId, conv.FriendName, myName, text, timestamp, isFromMe: true);

        conv.Messages.Add(new PrivateMessageViewModel
        {
            SenderName = myName,
            Content = text,
            Timestamp = timestamp,
            IsFromMe = true,
        });
        conv.LastMessageTime = timestamp;

        ReplyText = "";
    }
}
