using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Web;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using FluentAvalonia.UI.Controls;
using HanumanInstitute.MvvmDialogs;
using HanumanInstitute.MvvmDialogs.Avalonia.Fluent;
using SymbolIconSource = FluentIcons.Avalonia.Fluent.SymbolIconSource;
using Symbol = FluentIcons.Common.Symbol;

using Xabbo.Core;
using Xabbo.Core.Game;
using Xabbo.Core.Events;
using Xabbo.Core.Messages.Incoming;
using Xabbo.Core.Messages.Outgoing;
using Xabbo.Interceptor;
using Xabbo.Messages;
using Xabbo.Messages.Flash;
using Xabbo.Services.Abstractions;
using Xabbo.Services;
using Xabbo.Utility;

namespace Xabbo.ViewModels;

public sealed class MessagesPageViewModel : PageViewModel
{
    public override string Header => TotalUnread > 0 ? $"Messages ({TotalUnread})" : "Messages";
    public override int BadgeCount => TotalUnread;
    public override IconSource? Icon { get; } = new SymbolIconSource { Symbol = Symbol.ChatMultiple };

    private readonly IUiContext _uiContext;
    private readonly IDialogService _dialogService;
    private readonly IInterceptor _interceptor;
    private readonly ILauncherService _launcher;
    private readonly IClipboardService _clipboard;
    private readonly FriendManager _friendManager;
    private readonly ProfileManager _profileManager;
    private readonly IPrivateMessageHistoryService _history;

    private readonly SourceCache<ConversationViewModel, Id> _cache = new(x => x.FriendId);
    private HashSet<Id> _hiddenIds = [];

    private readonly ReadOnlyObservableCollection<ConversationViewModel> _conversations;
    public ReadOnlyObservableCollection<ConversationViewModel> Conversations => _conversations;

    [Reactive] public ConversationViewModel? SelectedConversation { get; set; }
    [Reactive] public string ReplyText { get; set; } = "";
    [Reactive] public int TotalUnread { get; private set; }
    [Reactive] public bool IsActive { get; set; }

    private bool _isSendingFromHere;

    public ReactiveCommand<IList?, Unit> CopySelectedMessagesCmd { get; }
    public ReactiveCommand<Unit, Unit> SendReplyCmd { get; }
    public ReactiveCommand<Unit, Unit> FollowFriendCmd { get; }
    public ReactiveCommand<ConversationViewModel, Unit> HideConversationCmd { get; }
    public ReactiveCommand<ConversationViewModel, Unit> DeleteConversationCmd { get; }
    public ReactiveCommand<string, Unit> OpenProfileCmd { get; }

    public MessagesPageViewModel(
        IUiContext uiContext,
        IDialogService dialogService,
        IInterceptor interceptor,
        ILauncherService launcher,
        IClipboardService clipboard,
        FriendManager friendManager,
        ProfileManager profileManager,
        IPrivateMessageHistoryService history)
    {
        _uiContext = uiContext;
        _dialogService = dialogService;
        _interceptor = interceptor;
        _launcher = launcher;
        _clipboard = clipboard;
        _friendManager = friendManager;
        _profileManager = profileManager;
        _history = history;

        _cache
            .Connect()
            .AutoRefresh(x => x.LastMessageTime)
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
        CopySelectedMessagesCmd = ReactiveCommand.Create<IList?>(CopySelectedMessages);

        var canFollow = this.WhenAnyValue(x => x.SelectedConversation)
            .Select(conv => conv is not null);
        FollowFriendCmd = ReactiveCommand.Create(FollowFriend, canFollow);

        HideConversationCmd = ReactiveCommand.Create<ConversationViewModel>(HideConversation);
        DeleteConversationCmd = ReactiveCommand.CreateFromTask<ConversationViewModel>(DeleteConversationAsync);
        OpenProfileCmd = ReactiveCommand.CreateFromTask<string>(OpenProfile);

        this.WhenAnyValue(x => x.TotalUnread)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(Header));
                this.RaisePropertyChanged(nameof(BadgeCount));
            });

        this.WhenAnyValue(x => x.SelectedConversation)
            .Subscribe(conv =>
            {
                if (conv is null) return;
                if (!conv.IsHistoryLoaded && !conv.IsLoadingHistory)
                    _ = LoadConversationHistoryAsync(conv);
                if (!IsActive) return;
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
        _friendManager.FriendUpdated += OnFriendUpdated;
        _friendManager.MessageReceived += OnMessageReceived;
        _friendManager.RoomInviteReceived += OnRoomInviteReceived;
        _interceptor.Intercept<SendConsoleMessageMsg>(OnSentConsoleMessage);
        _interceptor.Intercept<ConsoleMessageMsg>(OnConsoleMessageEcho);

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        _hiddenIds = await _history.LoadHiddenConversationsAsync();
        await LoadHistoryAsync();
    }

    private void OnFriendUpdated(FriendUpdatedEventArgs e)
    {
        _uiContext.Invoke(() =>
        {
            var lookup = _cache.Lookup(e.Friend.Id);
            if (!lookup.HasValue) return;

            var conv = lookup.Value;
            conv.IsOnline = e.Friend.IsOnline;

            if (e.Previous.Figure != e.Friend.Figure && !string.IsNullOrEmpty(e.Friend.Figure))
                conv.FriendFigure = e.Friend.Figure;

            if (!string.IsNullOrEmpty(e.Friend.Name) && e.Friend.Name != conv.FriendName)
            {
                conv.FriendName = e.Friend.Name;
                _history.UpdateFriendName(conv.FriendId, e.Friend.Name);
            }
        });
    }

    private void OnFriendsLoaded()
    {
        // Met à jour les figures et statut online des conversations déjà créées depuis l'historique
        _uiContext.Invoke(() =>
        {
            foreach (var conv in _cache.Items)
            {
                var friend = _friendManager.GetFriend(conv.FriendId);
                if (friend is null) continue;

                conv.IsOnline = friend.IsOnline;

                if (string.IsNullOrEmpty(conv.FriendFigure) && !string.IsNullOrEmpty(friend.Figure))
                    conv.FriendFigure = friend.Figure;

                if (!string.IsNullOrEmpty(friend.Name) && friend.Name != conv.FriendName)
                {
                    conv.FriendName = friend.Name;
                    _history.UpdateFriendName(conv.FriendId, friend.Name);
                }
            }
        });

        // Les conversations dont l'ami n'est plus dans la liste (renommé/supprimé) ne peuvent
        // pas être rafraîchies via FriendManager : on interroge le profil par ID.
        _ = RefreshStaleConversationsAsync();
    }

    private async Task RefreshStaleConversationsAsync()
    {
        if (!_interceptor.Session.Is(ClientType.Modern))
            return;

        var stale = _cache.Items
            .Where(conv => _friendManager.GetFriend(conv.FriendId) is null)
            .ToList();

        foreach (var conv in stale)
        {
            try
            {
                var profile = await _interceptor.RequestAsync(new GetProfileMsg(conv.FriendId), block: false);
                if (profile is null) continue;

                _uiContext.Invoke(() =>
                {
                    if (!_cache.Lookup(conv.FriendId).HasValue) return;

                    if (!string.IsNullOrEmpty(profile.Figure))
                        conv.FriendFigure = profile.Figure;

                    if (!string.IsNullOrEmpty(profile.Name) && profile.Name != conv.FriendName)
                    {
                        conv.FriendName = profile.Name;
                        _history.UpdateFriendName(conv.FriendId, profile.Name);
                    }
                });
            }
            catch
            {
                // Profil indisponible / timeout : on garde les valeurs historiques.
            }
        }
    }

    private async Task LoadHistoryAsync()
    {
        var records = await _history.LoadAllAsync();
        _uiContext.Invoke(() =>
        {
            foreach (var r in records)
            {
                if (_hiddenIds.Contains(r.FriendId))
                    continue;

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
                        IsOnline = _friendManager.GetFriend(r.FriendId)?.IsOnline ?? false,
                    };
                    _cache.AddOrUpdate(conv);
                }
                conv.AddMessage(new PrivateMessageViewModel
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

        if (_hiddenIds.Remove(friendId))
            _history.UnhideConversation(friendId);

        var friend = _friendManager.GetFriend(friendId);
        var vm = new ConversationViewModel
        {
            FriendId = friendId,
            FriendName = friendName,
            FriendFigure = friend?.Figure ?? "",
            IsOnline = friend?.IsOnline ?? false,
        };
        _cache.AddOrUpdate(vm);
        _ = LoadConversationHistoryAsync(vm);
        return vm;
    }

    private async Task LoadConversationHistoryAsync(ConversationViewModel conv)
    {
        conv.IsLoadingHistory = true;
        var records = await _history.LoadConversationAsync(conv.FriendId);
        _uiContext.Invoke(() =>
        {
            if (!_cache.Lookup(conv.FriendId).HasValue) return;

            // Preserve invitations (not persisted in DB) before replacing
            var invitations = conv.Messages.Where(m => m.IsInvitation).ToList();

            // Replace the preview message with full history + invitations merged by timestamp
            conv.Messages.Clear();
            int invIdx = 0;
            foreach (var r in records)
            {
                while (invIdx < invitations.Count && invitations[invIdx].Timestamp <= r.Timestamp)
                    conv.AddMessage(invitations[invIdx++]);

                conv.AddMessage(new PrivateMessageViewModel
                {
                    SenderName = r.SenderName,
                    Content = r.Content,
                    Timestamp = r.Timestamp,
                    IsFromMe = r.IsFromMe,
                });
            }
            while (invIdx < invitations.Count)
                conv.AddMessage(invitations[invIdx++]);

            // Flush messages that arrived during loading
            conv.IsLoadingHistory = false;
            conv.IsHistoryLoaded = true;
            foreach (var m in conv.PendingMessages)
            {
                if (!m.IsInvitation)
                    _history.Enqueue(conv.FriendId, conv.FriendName, m.SenderName, m.Content, m.Timestamp, m.IsFromMe);
                conv.AddMessage(m);
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
                vm.AddMessage(msg);
            }

            UpdateLastMessageTime(vm, timestamp);

            if (!IsActive || !ReferenceEquals(SelectedConversation, vm))
            {
                vm.UnreadCount++;
                TotalUnread++;
            }
        });
    }

    private void OnRoomInviteReceived(RoomInviteEventArgs e)
    {
        var timestamp = DateTimeOffset.Now;

        _uiContext.Invoke(() =>
        {
            var vm = GetOrCreateConversation(e.Friend);
            var msg = new PrivateMessageViewModel
            {
                SenderName = e.Friend.Name,
                Content = e.Message,
                Timestamp = timestamp,
                IsFromMe = false,
                IsInvitation = true,
            };

            if (vm.IsLoadingHistory)
            {
                vm.PendingMessages.Add(msg);
            }
            else
            {
                vm.AddMessage(msg);
            }

            UpdateLastMessageTime(vm, timestamp);

            if (!IsActive || !ReferenceEquals(SelectedConversation, vm))
            {
                vm.UnreadCount++;
                TotalUnread++;
            }
        });
    }

    private void FollowFriend()
    {
        var conv = SelectedConversation;
        if (conv is null) return;
        _interceptor.Send(Out.FollowFriend, conv.FriendId);
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
                conv.AddMessage(pm);
            }
            UpdateLastMessageTime(conv, timestamp);

            // Replied from game = conversation is read
            TotalUnread -= conv.UnreadCount;
            if (TotalUnread < 0) TotalUnread = 0;
            conv.UnreadCount = 0;
        });
    }

    // Habbicons envoyés par soi-même reviennent en écho NewConsole (SenderId == mon id)
    // au lieu de passer par un SendMsg outgoing. On les affiche ici comme IsFromMe.
    // Le texte garde son chemin via OnSentConsoleMessage, donc on ignore MessageType == 0.
    private void OnConsoleMessageEcho(ConsoleMessageMsg msg)
    {
        var m = msg.Message;

        var myId = _profileManager.UserData?.Id ?? default;
        if (m.SenderId != myId) return;
        if (m.MessageType == 0) return;

        // ChatId = l'autre participant (ici le destinataire). On ne peut MP qu'un ami.
        var friend = _friendManager.GetFriend(m.ChatId);
        if (friend is null) return;

        var myName = _profileManager.UserData?.Name ?? "Me";
        var timestamp = DateTimeOffset.Now;

        _uiContext.Invoke(() =>
        {
            var conv = GetOrCreateConversation(friend);
            var pm = new PrivateMessageViewModel
            {
                SenderName = myName,
                Content = m.Content,
                Timestamp = timestamp,
                IsFromMe = true,
            };
            if (conv.IsLoadingHistory)
            {
                conv.PendingMessages.Add(pm);
            }
            else
            {
                _history.Enqueue(friend.Id, friend.Name, myName, m.Content, timestamp, isFromMe: true);
                conv.AddMessage(pm);
            }
            UpdateLastMessageTime(conv, timestamp);
        });
    }

    private ConversationViewModel GetOrCreateConversation(IFriend friend)
    {
        var conv = GetOrCreateConversationById(friend.Id, friend.Name);
        if (string.IsNullOrEmpty(conv.FriendFigure) && !string.IsNullOrEmpty(friend.Figure))
            conv.FriendFigure = friend.Figure;
        conv.IsOnline = friend.IsOnline;
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
        _hiddenIds.Add(conv.FriendId);
        _history.HideConversation(conv.FriendId);
        _cache.RemoveKey(conv.FriendId);
    }

    private async Task DeleteConversationAsync(ConversationViewModel conv)
    {
        if (ReferenceEquals(SelectedConversation, conv))
            SelectedConversation = null;
        TotalUnread -= conv.UnreadCount;
        if (TotalUnread < 0) TotalUnread = 0;
        _hiddenIds.Remove(conv.FriendId);
        _history.UnhideConversation(conv.FriendId);
        _cache.RemoveKey(conv.FriendId);
        await _history.DeleteConversationAsync(conv.FriendId);
    }

    private async Task OpenProfile(string type)
    {
        var conv = SelectedConversation;
        if (conv is null) return;

        switch (type)
        {
            case "game":
                var profile = await _interceptor.RequestAsync(new GetProfileMsg(conv.FriendId, Open: true), block: false);
                if (!profile.DisplayInClient)
                {
                    await _dialogService.ShowContentDialogAsync(
                        _dialogService.CreateViewModel<MainViewModel>(),
                        new ContentDialogSettings
                        {
                            Title = "Failed to open profile",
                            Content = $"{conv.FriendName}'s profile is not visible.",
                            PrimaryButtonText = "OK",
                        }
                    );
                }
                break;
            case "web":
                _launcher.Launch($"https://{_interceptor.Session.Hotel.WebHost}/profile/{HttpUtility.UrlEncode(conv.FriendName)}");
                break;
            case "habbowidgets":
                _launcher.Launch($"https://www.habbowidgets.com/habinfo/{_interceptor.Session.Hotel.Domain}/{HttpUtility.UrlEncode(conv.FriendName)}");
                break;
        }
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

        conv.AddMessage(new PrivateMessageViewModel
        {
            SenderName = myName,
            Content = text,
            Timestamp = timestamp,
            IsFromMe = true,
        });
        UpdateLastMessageTime(conv, timestamp);

        ReplyText = "";
    }

    private void CopySelectedMessages(IList? selected)
    {
        if (selected is null) return;
        var items = selected
            .Cast<PrivateMessageViewModel?>()
            .Where(x => x is not null)
            .ToList();
        if (items.Count == 0) return;
        _clipboard.SetText(string.Join("\n", items));
    }

    /// <summary>
    /// Updates LastMessageTime while preserving the selected conversation,
    /// because DynamicData SortAndBind re-sorts on AutoRefresh which resets ListBox selection.
    /// </summary>
    private void UpdateLastMessageTime(ConversationViewModel conv, DateTimeOffset timestamp)
    {
        var selected = SelectedConversation;
        conv.LastMessageTime = timestamp;
        if (selected is not null && !ReferenceEquals(SelectedConversation, selected))
            SelectedConversation = selected;
    }
}
