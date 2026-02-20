using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Web;
using System.Windows.Input;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using Splat;
using Avalonia.Controls.Selection;
using FluentIcons.Common;
using FluentIcons.Avalonia.Fluent;
using HanumanInstitute.MvvmDialogs;

using Xabbo.Core;
using Xabbo.Core.Game;
using Xabbo.Core.Events;
using Xabbo.Core.Messages.Incoming;
using Xabbo.Core.Messages.Outgoing;
using Xabbo.Configuration;
using Xabbo.Controllers;
using Xabbo.Exceptions;
using Xabbo.Extension;
using Xabbo.Models;
using Xabbo.Services;
using Xabbo.Services.Abstractions;
using Xabbo.Utility;
using Xabbo.Command;
using Xabbo.Command.Modules;
using Xabbo.Components;

using IconSource = FluentAvalonia.UI.Controls.IconSource;

namespace Xabbo.ViewModels;

public class ChatPageViewModel : PageViewModel
{
    public override string Header => "Chat";
    public override IconSource? Icon { get; } = new SymbolIconSource { Symbol = Symbol.Chat };

    private readonly IConfigProvider<AppConfig> _settingsProvider;
    private AppConfig Settings => _settingsProvider.Value;

    private readonly IExtension _ext;
    private readonly IDialogService _dialog;
    private readonly IClipboardService _clipboard;
    private readonly ILauncherService _launcher;
    private readonly IGameStateService _gameState;
    private readonly IFigureConverterService _figureConverter;
    private readonly IProfanityFilterService _profanityFilter;
    private readonly IFileExportService _fileExport;
    private readonly IChatHistoryService _chatHistory;

    private readonly RoomManager _roomManager;
    private readonly ProfileManager _profileManager;
    private readonly RoomModerationController _moderation;
    private readonly XabbotComponent _xabbot;
    private ModerationCommands? _moderationCommands;

    public ChatLogConfig Config => Settings.Chat.Log;

    // Global counter for received messages
    private long _currentMessageId;
    
    private readonly SourceCache<ChatLogEntryViewModel, long> _cache = new(x => x.EntryId);

    private readonly ReadOnlyObservableCollection<ChatLogEntryViewModel> _messages;
    public ReadOnlyObservableCollection<ChatLogEntryViewModel> Messages => _messages;

    [Reactive] public IList<ChatMessageViewModel>? ContextSelection { get; set; }

    private readonly ObservableAsPropertyHelper<string?> _selectedUserName;
    public string? SelectedUserName => _selectedUserName.Value;

    public ReactiveCommand<Unit, Unit> CopySelectedEntriesCmd { get; }

    public ReactiveCommand<Unit, Unit> FindUserCmd { get; }
    public ReactiveCommand<string, Unit> CopyUserFieldCmd { get; }
    public ReactiveCommand<Unit, Unit> GoToMessageCmd { get; }
    public ReactiveCommand<string, Task> OpenUserProfileCmd { get; }

    public ReactiveCommand<string, Task> MuteUsersCmd { get; }
    public ReactiveCommand<Unit, Task> KickUsersCmd { get; }
    public ReactiveCommand<BanDuration, Task> BanUsersCmd { get; }
    public ReactiveCommand<Unit, Task> BounceUsersCmd { get; }

    // Quick mute/kick/ban by username (for profanity message buttons)
    public ReactiveCommand<string, Task> KickUserByNameCmd { get; }
    public ReactiveCommand<(string Name, int Minutes), Task> MuteUserByNameCmd { get; }
    public ReactiveCommand<(string Name, BanDuration Duration), Task> BanUserByNameCmd { get; }

    public ReactiveCommand<Unit, Task> AddToProfanityFilterCmd { get; }
    public ReactiveCommand<string, Unit> RemoveFromProfanityFilterCmd { get; }

    public ReactiveCommand<string, Task> ExportChatCmd { get; }

    // Chat input
    [Reactive] public ChatType ChatInputMode { get; set; } = ChatType.Talk;
    [Reactive] public string ChatInputText { get; set; } = "";
    [Reactive] public string WhisperRecipient { get; set; } = "";
    [Reactive] public bool IsInRoom { get; set; }

    private readonly ObservableAsPropertyHelper<bool> _isWhisperMode;
    public bool IsWhisperMode => _isWhisperMode.Value;

    // Tracks the recipient of the last sent whisper, to label outgoing whisper echoes
    private string? _lastSentWhisperRecipient;

    // Whisper autocomplete
    private readonly Dictionary<string, DateTime> _recentWhisperRecipients = new(StringComparer.OrdinalIgnoreCase);
    [Reactive] public ObservableCollection<WhisperSuggestionItem> WhisperSuggestions { get; set; } = [];
    [Reactive] public ObservableCollection<WhisperSuggestionItem> RecentWhisperList { get; set; } = [];

    private readonly Subject<Unit> _avatarPresenceChanged = new();
    private readonly ObservableAsPropertyHelper<bool> _isRecipientInRoom;
    public bool IsRecipientInRoom => _isRecipientInRoom.Value;

    public ReactiveCommand<string, Unit> SelectRecentRecipientCmd { get; }
    public ReactiveCommand<Unit, Unit> SendChatCmd { get; }
    public ReactiveCommand<Unit, Unit> WhisperToSelectedCmd { get; }

    public SelectionModel<ChatLogEntryViewModel> Selection { get; } = new SelectionModel<ChatLogEntryViewModel>()
    {
        SingleSelect = false
    };

    [Reactive] public string? FilterText { get; set; }
    [Reactive] public bool WhispersOnly { get; set; }
    [Reactive] public bool ProfanityOnly { get; set; }

    private readonly ObservableAsPropertyHelper<bool> _hasActiveFilter;
    public bool HasActiveFilter => _hasActiveFilter.Value;

    // Action to scroll to a specific message - set from view
    public Action<ChatLogEntryViewModel>? ScrollToMessageAction { get; set; }

    // History search properties
    [Reactive] public string? HistorySearchUser { get; set; }
    [Reactive] public string? HistorySearchKeyword { get; set; }
    [Reactive] public bool HistorySearchProfanityOnly { get; set; }
    [Reactive] public bool HistorySearchWhispersOnly { get; set; }
    [Reactive] public DateTime? HistorySearchFromDate { get; set; }
    [Reactive] public TimeSpan? HistorySearchFromTime { get; set; }
    [Reactive] public DateTime? HistorySearchToDate { get; set; }
    [Reactive] public TimeSpan? HistorySearchToTime { get; set; }

    private readonly ObservableCollection<ChatHistoryEntry> _historyResults = [];
    public ObservableCollection<ChatHistoryEntry> HistoryResults => _historyResults;

    [Reactive] public int HistoryResultsTotal { get; set; }
    [Reactive] public string? HistoryErrorMessage { get; set; }
    [Reactive] public string HistoryResultsText { get; set; } = "Results: 0";

    // Current search parameters (for export)
    private string? _lastSearchUser;
    private string? _lastSearchKeyword;
    private bool _lastSearchProfanityOnly;
    private bool _lastSearchWhispersOnly;
    private DateTime? _lastSearchFromDate;
    private DateTime? _lastSearchToDate;

    public ReactiveCommand<Unit, Unit> SearchHistoryCmd { get; }
    public ReactiveCommand<string, Task> ExportHistoryCmd { get; }
    public ReactiveCommand<Unit, Unit> CopyHistoryEntriesCmd { get; }
    public ReactiveCommand<BanDuration, Task> BanHistoryUserCmd { get; }

    public SelectionModel<ChatHistoryEntry> HistorySelection { get; } = new SelectionModel<ChatHistoryEntry>()
    {
        SingleSelect = false
    };

    // Scroll to bottom command - will be set from view
    [Reactive] public ICommand? ScrollToBottomCmd { get; set; }

    // Action to open history flyout - set from view
    public Action? OpenHistoryFlyoutAction { get; set; }

    public ReactiveCommand<Unit, Unit> SearchUserInHistoryCmd { get; }

    // Clear chat commands
    public ReactiveCommand<Unit, Unit> ClearAllMessagesCmd { get; }
    public ReactiveCommand<Unit, Unit> KeepLast60MinCmd { get; }

    [DependencyInjectionConstructor]
    public ChatPageViewModel(
        IExtension ext,
        IConfigProvider<AppConfig> settingsProvider,
        IDialogService dialog,
        IClipboardService clipboard,
        ILauncherService launcher,
        IGameStateService gameState,
        IFigureConverterService figureConverter,
        IProfanityFilterService profanityFilter,
        IFileExportService fileExport,
        IChatHistoryService chatHistory,
        RoomManager roomManager,
        ProfileManager profileManager,
        RoomModerationController moderation,
        XabbotComponent xabbot)
    {
        _ext = ext;
        _settingsProvider = settingsProvider;
        _dialog = dialog;
        _clipboard = clipboard;
        _launcher = launcher;
        _gameState = gameState;
        _figureConverter = figureConverter;
        _profanityFilter = profanityFilter;
        _fileExport = fileExport;
        _chatHistory = chatHistory;
        _roomManager = roomManager;
        _profileManager = profileManager;
        _moderation = moderation;
        _xabbot = xabbot;

        _roomManager.Entered += OnEnteredRoom;
        _roomManager.Left += () => IsInRoom = false;
        _roomManager.AvatarAdded += OnAvatarAdded;
        _roomManager.AvatarRemoved += OnAvatarRemoved;
        _roomManager.AvatarChat += RoomManager_AvatarChat;
        _roomManager.AvatarUpdated += RoomManager_AvatarUpdated;
        Observable
            .FromEvent(
                h => _profanityFilter.PatternsChanged += h,
                h => _profanityFilter.PatternsChanged -= h)
            .Throttle(TimeSpan.FromMilliseconds(150))
            .Subscribe(_ => OnProfanityPatternsChanged());

        // Context selection observables for command can-execute
        var hasSingleContextMessage = this
            .WhenAnyValue(x => x.ContextSelection)
            .Select(selection => selection is { Count: 1 })
            .StartWith(false)
            .ObserveOn(RxApp.MainThreadScheduler);

        var hasAnyContextMessage = this
            .WhenAnyValue(x => x.ContextSelection)
            .Select(selection => selection is { Count: > 0 })
            .StartWith(false)
            .ObserveOn(RxApp.MainThreadScheduler);

        // Check if selection contains at least one user other than self (for moderation)
        var hasOtherUsers = Observable.CombineLatest(
            this.WhenAnyValue(x => x.ContextSelection),
            _profileManager.WhenAnyValue(x => x.UserData),
            (selection, userData) =>
                selection is { Count: > 0 } &&
                selection.Any(m => m.Name != userData?.Name)
        ).StartWith(false).ObserveOn(RxApp.MainThreadScheduler);

        _selectedUserName = this
            .WhenAnyValue(x => x.ContextSelection)
            .Select(selection => selection is [var msg] ? msg.Name : null)
            .ToProperty(this, x => x.SelectedUserName);

        // HasActiveFilter: true when any filter is active
        _hasActiveFilter = this
            .WhenAnyValue(x => x.FilterText, x => x.WhispersOnly, x => x.ProfanityOnly)
            .Select(t => !string.IsNullOrWhiteSpace(t.Item1) || t.Item2 || t.Item3)
            .ToProperty(this, x => x.HasActiveFilter);

        // CanExecute for GoToMessage: message selected AND filter active
        var canGoToMessage = Observable.CombineLatest(
            hasSingleContextMessage,
            this.WhenAnyValue(x => x.HasActiveFilter),
            (hasMsg, hasFilter) => hasMsg && hasFilter
        ).ObserveOn(RxApp.MainThreadScheduler);

        var canMuteUsers = Observable.CombineLatest(
            hasOtherUsers,
            _moderation.WhenAnyValue(
                x => x.CurrentOperation,
                x => x.CanMute,
                (op, canMute) => op is RoomModerationController.ModerationType.None && canMute
            ).StartWith(false),
            (hasOthers, canModerate) => hasOthers && canModerate
        ).ObserveOn(RxApp.MainThreadScheduler);

        var canKickUsers = Observable.CombineLatest(
            hasOtherUsers,
            _moderation.WhenAnyValue(
                x => x.CurrentOperation,
                x => x.CanKick,
                (op, canKick) => op is RoomModerationController.ModerationType.None && canKick
            ).StartWith(false),
            (hasOthers, canModerate) => hasOthers && canModerate
        ).ObserveOn(RxApp.MainThreadScheduler);

        var canBanUsers = Observable.CombineLatest(
            hasOtherUsers,
            _moderation.WhenAnyValue(
                x => x.CurrentOperation,
                x => x.CanBan,
                (op, canBan) => op is RoomModerationController.ModerationType.None && canBan
            ).StartWith(false),
            (hasOthers, canModerate) => hasOthers && canModerate
        ).ObserveOn(RxApp.MainThreadScheduler);

        var canBounceUsers = Observable.CombineLatest(
            hasOtherUsers,
            _moderation.WhenAnyValue(
                x => x.CurrentOperation,
                x => x.RightsLevel,
                x => x.IsOwner,
                (op, rightsLevel, isOwner) =>
                    op is RoomModerationController.ModerationType.None &&
                    (rightsLevel is RightsLevel.Owner || isOwner)
            ).StartWith(false),
            (hasOthers, canModerate) => hasOthers && canModerate
        ).ObserveOn(RxApp.MainThreadScheduler);

        // Initialize commands
        FindUserCmd = ReactiveCommand.Create(FindUser, hasSingleContextMessage);
        CopyUserFieldCmd = ReactiveCommand.Create<string>(CopyUserField, hasSingleContextMessage);
        OpenUserProfileCmd = ReactiveCommand.Create<string, Task>(OpenUserProfile, hasSingleContextMessage);
        GoToMessageCmd = ReactiveCommand.Create(GoToMessage, canGoToMessage);

        MuteUsersCmd = ReactiveCommand.Create<string, Task>(MuteUsersAsync, canMuteUsers);
        KickUsersCmd = ReactiveCommand.Create<Task>(KickUsersAsync, canKickUsers);
        BanUsersCmd = ReactiveCommand.Create<BanDuration, Task>(BanUsersAsync, canBanUsers);
        BounceUsersCmd = ReactiveCommand.Create<Task>(BounceUsersAsync, canBounceUsers);

        // Quick mute/kick/ban by username (for profanity message buttons)
        KickUserByNameCmd = ReactiveCommand.Create<string, Task>(KickUserByNameAsync);
        MuteUserByNameCmd = ReactiveCommand.Create<(string Name, int Minutes), Task>(MuteUserByNameAsync);
        BanUserByNameCmd = ReactiveCommand.Create<(string Name, BanDuration Duration), Task>(BanUserByNameAsync);

        AddToProfanityFilterCmd = ReactiveCommand.Create<Task>(AddToProfanityFilterAsync, hasSingleContextMessage);
        RemoveFromProfanityFilterCmd = ReactiveCommand.Create<string>(RemoveFromProfanityFilter);

        ExportChatCmd = ReactiveCommand.Create<string, Task>(ExportChatAsync);

        // History commands
        SearchHistoryCmd = ReactiveCommand.CreateFromTask(SearchHistoryAsync);
        ExportHistoryCmd = ReactiveCommand.Create<string, Task>(ExportHistoryAsync);
        CopyHistoryEntriesCmd = ReactiveCommand.Create(CopyHistoryEntries);
        var canBanHistoryUser = Observable
            .FromEventPattern(HistorySelection, nameof(HistorySelection.SelectionChanged))
            .Select(_ => HistorySelection.SelectedItem is ChatHistoryEntry { CanBan: true })
            .StartWith(false);
        BanHistoryUserCmd = ReactiveCommand.Create<BanDuration, Task>(BanHistoryUserAsync, canBanHistoryUser);
        SearchUserInHistoryCmd = ReactiveCommand.Create(SearchUserInHistory, hasSingleContextMessage);

        // Clear chat commands
        ClearAllMessagesCmd = ReactiveCommand.Create(ClearAllMessages);
        KeepLast60MinCmd = ReactiveCommand.Create(KeepLast60Min);

        // Scroll to bottom when unchecking a filter
        this.WhenAnyValue(x => x.WhispersOnly)
            .Skip(1)
            .Where(v => !v)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => ScrollToBottomCmd?.Execute(null));

        this.WhenAnyValue(x => x.ProfanityOnly)
            .Skip(1)
            .Where(v => !v)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => ScrollToBottomCmd?.Execute(null));

        // Filter by text search, whispers only, and profanity only
        var filterPredicate = this
            .WhenAnyValue(x => x.FilterText, x => x.WhispersOnly, x => x.ProfanityOnly)
            .Select(tuple => CreateCombinedFilter(tuple.Item1, tuple.Item2, tuple.Item3));

        _cache
            .Connect()
            .Filter(filterPredicate)
            .ObserveOn(RxApp.MainThreadScheduler)
            .SortAndBind(out _messages, SortExpressionComparer<ChatLogEntryViewModel>.Ascending(x => x.EntryId))
            .Subscribe();

        CopySelectedEntriesCmd = ReactiveCommand.Create(CopySelectedEntries);

        // Chat input
        _isWhisperMode = this
            .WhenAnyValue(x => x.ChatInputMode)
            .Select(mode => mode == ChatType.Whisper)
            .ToProperty(this, x => x.IsWhisperMode);

        var canSendChat = this
            .WhenAnyValue(x => x.IsInRoom, x => x.ChatInputText,
                (inRoom, text) => inRoom && !string.IsNullOrEmpty(text))
            .ObserveOn(RxApp.MainThreadScheduler);

        SendChatCmd = ReactiveCommand.Create(SendChat, canSendChat);
        WhisperToSelectedCmd = ReactiveCommand.Create(WhisperToSelected, hasSingleContextMessage);
        SelectRecentRecipientCmd = ReactiveCommand.Create<string>(name => WhisperRecipient = name);

        // Update whisper suggestions when recipient text changes while in whisper mode
        this.WhenAnyValue(x => x.WhisperRecipient, x => x.IsWhisperMode)
            .Where(t => t.Item2)
            .Throttle(TimeSpan.FromMilliseconds(100))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(t => UpdateWhisperSuggestions(t.Item1));

        // Live indicator: is the current recipient in the room?
        // Re-evaluates when the recipient name changes OR when avatars enter/leave the room.
        _isRecipientInRoom = this
            .WhenAnyValue(x => x.WhisperRecipient)
            .CombineLatest(_avatarPresenceChanged.StartWith(Unit.Default), (name, _) => name)
            .Throttle(TimeSpan.FromMilliseconds(100))
            .Select(name => !string.IsNullOrWhiteSpace(name)
                && _roomManager.Room?.TryGetUserByName(name.Trim(), out _) == true)
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToProperty(this, x => x.IsRecipientInRoom);
    }

    // Combined filter for text search, whispers only, and profanity only
    private Func<ChatLogEntryViewModel, bool> CreateCombinedFilter(string? filterText, bool whispersOnly, bool profanityOnly)
    {
        var hasTextFilter = !string.IsNullOrWhiteSpace(filterText);
        var keywords = hasTextFilter ? ParseKeywords(filterText!) : null;

        return vm =>
        {
            // Whispers only filter
            if (whispersOnly)
            {
                if (vm is not ChatMessageViewModel chatMsg || !chatMsg.IsWhisper)
                    return false;
            }

            // Profanity only filter
            if (profanityOnly)
            {
                if (vm is not ChatMessageViewModel chatMsg || !chatMsg.HasProfanity)
                    return false;
            }

            // Text filter
            if (hasTextFilter && keywords is not null)
            {
                return CheckKeywords(vm, keywords);
            }

            return true;
        };
    }

    private bool CheckKeywords(ChatLogEntryViewModel vm, List<string> keywords)
    {
        return vm switch
        {
            ChatMessageViewModel chat =>
                keywords.Any(keyword =>
                    chat.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    chat.Message.Contains(keyword, StringComparison.OrdinalIgnoreCase)),
            ChatLogAvatarActionViewModel action =>
                keywords.Any(keyword =>
                    action.UserName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    action.Action.Contains(keyword, StringComparison.OrdinalIgnoreCase)),
            ChatLogRoomEntryViewModel room => 
                 keywords.Any(keyword =>
                    room.RoomName.Contains(keyword, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    private void CopySelectedEntries()
    {
        _clipboard.SetText(string.Join("\n", Selection.SelectedItems));
    }

    public void SendChat()
    {
        var text = ChatInputText;
        if (string.IsNullOrEmpty(text)) return;

        switch (ChatInputMode)
        {
            case ChatType.Talk:
                _ext.Send(new ChatMsg(ChatType.Talk, text));
                break;
            case ChatType.Shout:
                _ext.Send(new ChatMsg(ChatType.Shout, text));
                break;
            case ChatType.Whisper:
                var recipient = WhisperRecipient?.Trim();
                if (string.IsNullOrEmpty(recipient)) return;
                _lastSentWhisperRecipient = recipient;
                _ext.Send(new WhisperMsg(recipient, text));
                RecordWhisperRecipient(recipient);
                if (_roomManager.Room?.TryGetUserByName(recipient, out _) != true)
                    _xabbot.ShowMessage($"'{recipient}' is not in this room. Your whisper was sent but won't be seen.");
                break;
        }

        ChatInputText = "";
    }

    private void WhisperToSelected()
    {
        if (SelectedUserName is { } name)
        {
            ChatInputMode = ChatType.Whisper;
            WhisperRecipient = name;
        }
    }

    private void UpdateWhisperSuggestions(string? filterText)
    {
        var roomUsers = _roomManager.Room?.Users;
        if (roomUsers is null)
        {
            WhisperSuggestions = [];
            return;
        }

        var selfName = _profileManager.UserData?.Name;
        var suggestions = new List<WhisperSuggestionItem>();

        foreach (var user in roomUsers)
        {
            if (user.Name.Equals(selfName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(filterText) &&
                !user.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase))
                continue;

            suggestions.Add(new WhisperSuggestionItem
            {
                Name = user.Name,
                IsInRoom = true,
                IsRecent = false,
            });
        }

        WhisperSuggestions = new ObservableCollection<WhisperSuggestionItem>(suggestions);
    }

    private void UpdateRecentWhisperList()
    {
        var cutoff = DateTime.Now.AddHours(-1);
        var roomUsers = _roomManager.Room?.Users;

        var recents = _recentWhisperRecipients
            .Where(kv => kv.Value >= cutoff)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new WhisperSuggestionItem
            {
                Name = kv.Key,
                IsInRoom = roomUsers?.Any(u =>
                    u.Name.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)) ?? false,
                IsRecent = true,
            })
            .ToList();

        RecentWhisperList = new ObservableCollection<WhisperSuggestionItem>(recents);
    }

    private void RecordWhisperRecipient(string name)
    {
        _recentWhisperRecipients[name] = DateTime.Now;

        // Prune entries older than 1 hour
        var cutoff = DateTime.Now.AddHours(-1);
        var expired = _recentWhisperRecipients
            .Where(kv => kv.Value < cutoff)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in expired)
            _recentWhisperRecipients.Remove(key);

        UpdateRecentWhisperList();
    }

    private async Task ExportChatAsync(string format)
    {
        var messages = Messages.ToList();
        if (messages.Count == 0)
        {
            await _dialog.ShowAsync("Export", "No messages to export.");
            return;
        }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string? filePath;

        if (format == "json")
        {
            var exportData = messages.Select(vm => vm switch
            {
                ChatMessageViewModel chat => new
                {
                    Type = "message",
                    chat.Timestamp,
                    chat.Name,
                    chat.Message,
                    ChatType = chat.Type.ToString(),
                    chat.IsWhisper,
                    chat.HasProfanity
                } as object,
                ChatLogAvatarActionViewModel action => new
                {
                    Type = "action",
                    action.Timestamp,
                    action.UserName,
                    action.Action
                },
                ChatLogRoomEntryViewModel room => new
                {
                    Type = "room",
                    room.Timestamp,
                    room.RoomName,
                    room.RoomOwner
                },
                _ => null
            }).Where(x => x is not null).ToList();

            filePath = await _fileExport.ExportJsonAsync($"chat_export_{timestamp}.json", exportData);
        }
        else
        {
            var sb = new StringBuilder();
            foreach (var vm in messages)
            {
                sb.AppendLine(vm.ToString());
            }
            filePath = await _fileExport.ExportTextAsync($"chat_export_{timestamp}.txt", sb.ToString());
        }

        if (filePath is not null)
        {
            _xabbot.ShowMessage($"Chat exported to {Path.GetFileName(filePath)}");
        }
    }

    private async void SearchUserInHistory()
    {
        if (ContextSelection is not [var message])
            return;

        // Clear other filters and set the username
        HistorySearchUser = message.Name;
        HistorySearchKeyword = null;
        HistorySearchProfanityOnly = false;
        HistorySearchWhispersOnly = false;
        HistorySearchFromDate = null;
        HistorySearchFromTime = null;
        HistorySearchToDate = null;
        HistorySearchToTime = null;

        // Open the flyout and search
        OpenHistoryFlyoutAction?.Invoke();
        await SearchHistoryAsync();
    }

    private void ClearAllMessages()
    {
        _cache.Clear();
    }

    private void KeepLast60Min()
    {
        var cutoff = DateTime.Now.AddMinutes(-60);
        var toRemove = _cache.Items.Where(m => m.Timestamp < cutoff).Select(m => m.EntryId).ToList();
        _cache.RemoveKeys(toRemove);
    }

    private async Task SearchHistoryAsync()
    {
        HistoryErrorMessage = null;
        _historyResults.Clear();

        // Store search parameters for export
        _lastSearchUser = string.IsNullOrWhiteSpace(HistorySearchUser) ? null : HistorySearchUser;
        _lastSearchKeyword = string.IsNullOrWhiteSpace(HistorySearchKeyword) ? null : HistorySearchKeyword;
        _lastSearchProfanityOnly = HistorySearchProfanityOnly;
        _lastSearchWhispersOnly = HistorySearchWhispersOnly;

        // Combine date + time
        _lastSearchFromDate = HistorySearchFromDate;
        if (_lastSearchFromDate.HasValue && HistorySearchFromTime.HasValue)
            _lastSearchFromDate = _lastSearchFromDate.Value.Date + HistorySearchFromTime.Value;

        _lastSearchToDate = HistorySearchToDate;
        if (_lastSearchToDate.HasValue && HistorySearchToTime.HasValue)
            _lastSearchToDate = _lastSearchToDate.Value.Date + HistorySearchToTime.Value;

        var (results, totalCount) = await Task.Run(() => _chatHistory.SearchWithCount(
            userName: _lastSearchUser,
            keyword: _lastSearchKeyword,
            profanityOnly: _lastSearchProfanityOnly ? true : null,
            whispersOnly: _lastSearchWhispersOnly ? true : null,
            fromDate: _lastSearchFromDate,
            toDate: _lastSearchToDate,
            limit: 5000
        ));

        var ownName = _profileManager.UserData?.Name;
        foreach (var entry in results)
        {
            // Recalculate HasProfanity and MatchedWords for displayed results
            // This catches messages that should be flagged with newly added filter words
            if (!string.IsNullOrEmpty(entry.Message))
            {
                var matches = _profanityFilter.FindMatches(entry.Message);
                if (matches.Count > 0)
                {
                    entry.HasProfanity = true;
                    entry.MatchedWords = matches.Select(m => entry.Message.Substring(m.Start, m.Length)).Distinct().ToList();
                }
            }
            // Ban is only available on message entries that are not from the local user
            entry.CanBan = entry.Type == "message"
                && !string.IsNullOrEmpty(entry.Name)
                && !entry.Name.Equals(ownName, StringComparison.OrdinalIgnoreCase);
            _historyResults.Add(entry);
        }

        HistoryResultsTotal = totalCount;

        // Update results text
        if (totalCount > _historyResults.Count)
        {
            HistoryResultsText = $"Results: {totalCount} ({_historyResults.Count} loaded)";
        }
        else
        {
            HistoryResultsText = $"Results: {totalCount}";
        }

        if (_historyResults.Count == 0)
        {
            HistoryErrorMessage = "No results found for the specified criteria.";
        }
    }

    private void CopyHistoryEntries()
    {
        var selectedItems = HistorySelection.SelectedItems
            .Where(e => e is not null)
            .Cast<ChatHistoryEntry>()
            .OrderBy(e => e.Timestamp)
            .ToList();

        if (selectedItems.Count == 0)
            return;

        var sb = new StringBuilder();
        foreach (var entry in selectedItems)
        {
            var timestamp = entry.Timestamp.ToString("g"); // Short date + short time (locale-aware)
            var line = entry.Type switch
            {
                "message" => $"[{timestamp}] {entry.Name}: {entry.Message}",
                "action" => $"[{timestamp}] {entry.UserName} {entry.Action}",
                "room" => $"[{timestamp}] Entered room: {entry.RoomName} by {entry.RoomOwner}",
                _ => entry.DisplayText
            };
            sb.AppendLine(line);
        }

        _clipboard.SetText(sb.ToString().TrimEnd());
    }

    private Task BanHistoryUserAsync(BanDuration duration)
    {
        var entry = HistorySelection.SelectedItem as ChatHistoryEntry;
        var name = entry?.Name;
        if (string.IsNullOrEmpty(name))
            return Task.CompletedTask;
        return BanUserByNameAsync((name, duration));
    }

    private async Task ExportHistoryAsync(string format)
    {
        // Clear any previous error
        HistoryErrorMessage = null;

        if (HistoryResultsTotal == 0)
        {
            // Show error inside the flyout instead of a dialog
            HistoryErrorMessage = "No history entries to export. Run a search first.";
            return;
        }

        // Get ALL results (no limit) for export using the same search parameters
        var entries = _chatHistory.Search(
            userName: _lastSearchUser,
            keyword: _lastSearchKeyword,
            profanityOnly: _lastSearchProfanityOnly ? true : null,
            whispersOnly: _lastSearchWhispersOnly ? true : null,
            fromDate: _lastSearchFromDate,
            toDate: _lastSearchToDate,
            limit: null // No limit for export
        ).ToList();

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string? filePath;

        if (format == "json")
        {
            filePath = await _fileExport.ExportJsonAsync($"chat_history_{timestamp}.json", entries);
        }
        else
        {
            var sb = new StringBuilder();
            DateTime? currentDate = null;

            // Entries are ordered by timestamp descending, so reverse for chronological export
            foreach (var entry in entries.OrderBy(e => e.Timestamp))
            {
                var entryDate = entry.Timestamp.Date;

                // Add date separator when date changes
                if (currentDate != entryDate)
                {
                    if (currentDate.HasValue)
                    {
                        sb.AppendLine();
                    }
                    sb.AppendLine($"=== {entryDate:dddd, dd MMMM yyyy} ===");
                    sb.AppendLine();
                    currentDate = entryDate;
                }

                var line = entry.Type switch
                {
                    "message" => $"[{entry.Timestamp:HH:mm:ss}] {entry.Name}: {entry.Message}",
                    "action" => $"[{entry.Timestamp:HH:mm:ss}] {entry.UserName} {entry.Action}",
                    "room" => $"[{entry.Timestamp:HH:mm:ss}] Entered room: {entry.RoomName} by {entry.RoomOwner}",
                    _ => entry.ToString()
                };
                sb.AppendLine(line);
            }
            filePath = await _fileExport.ExportTextAsync($"chat_history_{timestamp}.txt", sb.ToString());
        }

        if (filePath is not null)
        {
            _xabbot.ShowMessage($"History exported to {Path.GetFileName(filePath)}");
        }
    }

    private long NextEntryId() => Interlocked.Increment(ref _currentMessageId);

    private static List<string> ParseKeywords(string filterText)
    {
        var keywords = new List<string>();
        var inQuotes = false;
        var currentWord = new StringBuilder();
        
        for (int i = 0; i < filterText.Length; i++)
        {
            char c = filterText[i];
            
            if (c == '"')
            {
                if (inQuotes)
                {
                    if (currentWord.Length > 0)
                    {
                        keywords.Add(currentWord.ToString());
                        currentWord.Clear();
                    }
                }
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (currentWord.Length > 0)
                {
                    keywords.Add(currentWord.ToString());
                    currentWord.Clear();
                }
            }
            else
            {
                currentWord.Append(c);
            }
        }
        
        if (currentWord.Length > 0)
        {
            keywords.Add(currentWord.ToString());
        }
        
        return keywords;
    }

    private void AppendLog(ChatLogEntryViewModel vm)
    {
        vm.EntryId = NextEntryId();
        _cache.AddOrUpdate(vm);
    }

    public void AppendModerationNotification(string userName, string action)
    {
        var vm = new ChatLogAvatarActionViewModel
        {
            UserName = userName,
            Action = action
        };
        AppendLog(vm);

        _chatHistory.AddEntry(new ChatHistoryEntry
        {
            Timestamp = vm.Timestamp,
            Type = "action",
            UserName = userName,
            Action = action
        });
    }

    private void OnAvatarAdded(AvatarEventArgs e)
    {
        if (e.Avatar.Type is not AvatarType.User)
            return;

        _avatarPresenceChanged.OnNext(Unit.Default);

        if (_roomManager.IsLoadingRoom || !Config.UserEntry)
            return;

        var vm = new ChatLogAvatarActionViewModel
        {
            UserName = e.Avatar.Name,
            Action = "entered the room"
        };
        AppendLog(vm);

        // Save to history
        _chatHistory.AddEntry(new ChatHistoryEntry
        {
            Timestamp = vm.Timestamp,
            Type = "action",
            UserName = e.Avatar.Name,
            Action = "entered the room"
        });

        if (IsWhisperMode)
        {
            UpdateWhisperSuggestions(WhisperRecipient);
            UpdateRecentWhisperList();
        }
    }

    private void OnAvatarRemoved(AvatarEventArgs e)
    {
        if (e.Avatar.Type is not AvatarType.User)
            return;

        _avatarPresenceChanged.OnNext(Unit.Default);

        if (_roomManager.IsLoadingRoom || !Config.UserEntry)
            return;

        var displayName = e.Avatar.Name.Equals(_profileManager.UserData?.Name) ? "You" : e.Avatar.Name;
        var vm = new ChatLogAvatarActionViewModel
        {
            UserName = displayName,
            Action = "left the room"
        };
        AppendLog(vm);

        // Save to history (use actual name, not "You")
        _chatHistory.AddEntry(new ChatHistoryEntry
        {
            Timestamp = vm.Timestamp,
            Type = "action",
            UserName = e.Avatar.Name,
            Action = "left the room"
        });

        if (IsWhisperMode)
        {
            UpdateWhisperSuggestions(WhisperRecipient);
            UpdateRecentWhisperList();
        }
    }

    private void RoomManager_AvatarUpdated(AvatarEventArgs e)
    {
        if (Config.Trades &&
            e.Avatar.PreviousUpdate is { IsTrading: bool wasTrading } &&
            e.Avatar.CurrentUpdate is { IsTrading: bool isTrading } &&
            isTrading != wasTrading)
        {
            var action = isTrading ? "started trading" : "stopped trading";
            var vm = new ChatLogAvatarActionViewModel
            {
                UserName = e.Avatar.Name,
                Action = action
            };
            AppendLog(vm);

            // Save to history
            _chatHistory.AddEntry(new ChatHistoryEntry
            {
                Timestamp = vm.Timestamp,
                Type = "action",
                UserName = e.Avatar.Name,
                Action = action
            });
        }
    }

    private void OnEnteredRoom(RoomEventArgs e)
    {
        IsInRoom = true;
        var roomName = e.Room.Data?.Name ?? "?";
        var roomOwner = e.Room.Data?.OwnerName ?? "?";
        var vm = new ChatLogRoomEntryViewModel
        {
            RoomName = roomName,
            RoomOwner = roomOwner
        };
        AppendLog(vm);

        // Save to history
        _chatHistory.AddEntry(new ChatHistoryEntry
        {
            Timestamp = vm.Timestamp,
            Type = "room",
            RoomName = roomName,
            RoomOwner = roomOwner
        });
    }

    private void RoomManager_AvatarChat(AvatarChatEventArgs e)
    {
        if (!Settings.Chat.Log.Normal && e is { Avatar.Type: AvatarType.User, ChatType: not ChatType.Whisper }) return;
        if (!Settings.Chat.Log.Whispers && e is { ChatType: ChatType.Whisper, BubbleStyle: not 34 }) return;
        if (!Settings.Chat.Log.Bots && e.Avatar.Type is AvatarType.PublicBot or AvatarType.PrivateBot) return;
        if (!Settings.Chat.Log.Pets && e.Avatar.Type is AvatarType.Pet) return;
        if (!Settings.Chat.Log.Wired && e is { ChatType: ChatType.Whisper, BubbleStyle: 34 }) return;

        IRoom? room = _roomManager.Room;
        if (room is null) return;

        string? figureString = null;
        if (e.Avatar.Type is not AvatarType.Pet)
        {
            if (_gameState.Session.Is(ClientType.Origins))
            {
                if (_figureConverter.TryConvertToModern(e.Avatar.Figure, out Figure? figure))
                    figureString = figure.ToString();
            }
            else
            {
                figureString = e.Avatar.Figure;
            }
        }

        var renderedMessage = H.RenderText(e.Message);
        var (hasProfanity, segments, matchedWords) = AnalyzeProfanity(renderedMessage);

        var isWhisper = e.ChatType == ChatType.Whisper && e.BubbleStyle != 34;
        var isOwnMessage = e.Avatar.Name.Equals(_profileManager.UserData?.Name, StringComparison.OrdinalIgnoreCase);
        var whisperRecipient = (isWhisper && isOwnMessage) ? _lastSentWhisperRecipient : null;

        var vm = new ChatMessageViewModel
        {
            Type = e.ChatType,
            BubbleStyle = e.BubbleStyle,
            Name = e.Avatar.Name,
            Message = renderedMessage,
            FigureString = figureString,
            HasProfanity = hasProfanity,
            MessageSegments = segments,
            MatchedWords = matchedWords,
            IsOwnMessage = isOwnMessage,
            WhisperRecipient = whisperRecipient,
        };
        AppendLog(vm);

        // Save to history (MatchedWords will be recalculated at search time for highlighting)
        _chatHistory.AddEntry(new ChatHistoryEntry
        {
            Timestamp = vm.Timestamp,
            Type = "message",
            Name = e.Avatar.Name,
            Message = renderedMessage,
            ChatType = e.ChatType.ToString(),
            IsWhisper = isWhisper,
            WhisperRecipient = whisperRecipient,
            HasProfanity = hasProfanity,
        });
    }

    /// <summary>
    /// Analyzes a message for profanity and returns segments for highlighting.
    /// </summary>
    private (bool HasProfanity, IReadOnlyList<MessageSegment>? Segments, IReadOnlyList<string>? MatchedWords) AnalyzeProfanity(string message)
    {
        var matches = _profanityFilter.FindMatches(message);
        if (matches.Count == 0)
            return (false, null, null);

        var segments = new List<MessageSegment>();
        // Base filter words (for "Remove from filter" menu)
        var matchedWords = matches.Select(m => m.MatchedWord).Distinct().ToList();
        int currentIndex = 0;

        foreach (var match in matches)
        {
            // Add text before the match
            if (match.Start > currentIndex)
            {
                segments.Add(new MessageSegment
                {
                    Text = message[currentIndex..match.Start],
                    IsProfanity = false
                });
            }

            // Add the profanity match
            segments.Add(new MessageSegment
            {
                Text = message.Substring(match.Start, match.Length),
                IsProfanity = true
            });

            currentIndex = match.Start + match.Length;
        }

        // Add remaining text after last match
        if (currentIndex < message.Length)
        {
            segments.Add(new MessageSegment
            {
                Text = message[currentIndex..],
                IsProfanity = false
            });
        }

        return (true, segments, matchedWords);
    }

    private void OnProfanityPatternsChanged()
    {
        // Update profanity flags in DB (background)
        _ = Task.Run(async () =>
        {
            try { await _chatHistory.UpdateProfanityFlagsAsync(_profanityFilter); }
            catch { /* DB update is best-effort */ }
        });

        // Snapshot messages and analyze off the UI thread, then dispatch updates
        var messages = _cache.Items.OfType<ChatMessageViewModel>().ToArray();
        _ = Task.Run(() =>
        {
            var updates = new List<(ChatMessageViewModel Msg, bool HasProfanity, IReadOnlyList<MessageSegment>? Segments, IReadOnlyList<string>? MatchedWords)>();
            foreach (var msg in messages)
            {
                var (hasProfanity, segments, matchedWords) = AnalyzeProfanity(msg.Message);
                updates.Add((msg, hasProfanity, segments, matchedWords));
            }

            // Apply property updates on UI thread
            Observable.Start(() =>
            {
                foreach (var (msg, hasProfanity, segments, matchedWords) in updates)
                {
                    msg.HasProfanity = hasProfanity;
                    msg.MessageSegments = segments;
                    msg.MatchedWords = matchedWords;
                }
            }, RxApp.MainThreadScheduler).Subscribe();
        });
    }

    // Context menu command implementations
    private void FindUser()
    {
        if (ContextSelection is not [var message])
            return;

        if (_roomManager.Room?.TryGetUserByName(message.Name, out var user) == true)
        {
            _ext.Send(new AvatarWhisperMsg("(click here to find)", user.Index));
        }
    }

    private void GoToMessage()
    {
        if (ContextSelection is not [var message])
            return;

        // Reset all filters
        FilterText = null;
        WhispersOnly = false;
        ProfanityOnly = false;

        // Scroll to the message after filters are applied
        ScrollToMessageAction?.Invoke(message);
    }

    private void CopyUserField(string field)
    {
        if (ContextSelection is not [var message])
            return;

        switch (field)
        {
            case "name":
                _clipboard.SetText(message.Name);
                break;
            case "message":
                _clipboard.SetText(message.Message);
                break;
            case "figure":
                if (!string.IsNullOrEmpty(message.FigureString))
                    _clipboard.SetText(message.FigureString);
                break;
        }
    }

    private async Task AddToProfanityFilterAsync()
    {
        if (ContextSelection is not [var message])
            return;

        // Create a TextBox for the dialog
        var textBox = new Avalonia.Controls.TextBox
        {
            Text = "",
            Watermark = "Enter word to add...",
            Width = 300
        };

        var stackPanel = new Avalonia.Controls.StackPanel
        {
            Spacing = 10,
            Children =
            {
                new Avalonia.Controls.TextBlock
                {
                    Text = $"Message: \"{message.Message}\"",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxWidth = 300
                },
                textBox
            }
        };

        var result = await _dialog.ShowContentDialogAsync(
            _dialog.CreateViewModel<MainViewModel>(),
            new HanumanInstitute.MvvmDialogs.Avalonia.Fluent.ContentDialogSettings
            {
                Title = "Add to profanity filter",
                Content = stackPanel,
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
            }
        );

        if (result == FluentAvalonia.UI.Controls.ContentDialogResult.Primary)
        {
            var word = textBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(word))
            {
                _profanityFilter.AddWord(word);
            }
        }
    }

    private void RemoveFromProfanityFilter(string word)
    {
        if (!string.IsNullOrWhiteSpace(word))
        {
            _profanityFilter.RemoveWord(word);
        }
    }

    private async Task OpenUserProfile(string type)
    {
        if (ContextSelection is not [var message])
            return;

        switch (type)
        {
            case "game":
                var profile = await _ext.RequestAsync(new GetProfileByNameMsg(message.Name), block: false);
                if (!profile.DisplayInClient)
                {
                    var result = await _dialog.ShowContentDialogAsync(
                        _dialog.CreateViewModel<MainViewModel>(),
                        new HanumanInstitute.MvvmDialogs.Avalonia.Fluent.ContentDialogSettings
                        {
                            Title = "Failed to open profile",
                            Content = $"{message.Name}'s profile is not visible.",
                            PrimaryButtonText = "OK",
                        }
                    );
                }
                break;
            case "web":
                _launcher.Launch($"https://{_ext.Session.Hotel.WebHost}/profile/{HttpUtility.UrlEncode(message.Name)}");
                break;
            case "habbowidgets":
                _launcher.Launch($"https://www.habbowidgets.com/habinfo/{_ext.Session.Hotel.Domain}/{HttpUtility.UrlEncode(message.Name)}");
                break;
        }
    }

    private IEnumerable<IUser> GetSelectedUsers()
    {
        if (ContextSelection is null || _roomManager.Room is null)
            yield break;

        foreach (var message in ContextSelection)
        {
            if (_roomManager.Room.TryGetUserByName(message.Name, out var user))
                yield return user;
        }
    }

    private Task MuteUsersAsync(string minutesStr) => TryModerate(() => MuteSelectedUsersAsync(int.Parse(minutesStr)));
    private Task KickUsersAsync() => TryModerate(async () =>
    {
        var users = GetSelectedUsers().ToList();
        await _moderation.KickUsersAsync(users);
        foreach (var user in users)
        {
            _xabbot.ShowMessage($"Kicked user '{user.Name}'");
            AppendModerationNotification(user.Name, "kicked");
        }
    });
    private Task BanUsersAsync(BanDuration duration) => TryModerate(() => BanSelectedUsersAsync(duration));
    private Task BounceUsersAsync() => TryModerate(async () =>
    {
        var users = GetSelectedUsers().ToList();
        await _moderation.BounceUsersAsync(users);
        foreach (var user in users)
        {
            _xabbot.ShowMessage($"Bounced user '{user.Name}'");
            AppendModerationNotification(user.Name, "bounced");
        }
    });

    private async Task MuteSelectedUsersAsync(int minutes)
    {
        if (ContextSelection is null)
            return;

        var usersInRoom = new List<IUser>();
        var usersNotInRoom = new List<string>();

        foreach (var message in ContextSelection)
        {
            if (_roomManager.Room is not null &&
                _roomManager.Room.TryGetUserByName(message.Name, out IUser? user))
            {
                usersInRoom.Add(user);
            }
            else
            {
                usersNotInRoom.Add(message.Name);
            }
        }

        // Mute users currently in the room
        if (usersInRoom.Count > 0)
        {
            await _moderation.MuteUsersAsync(usersInRoom, minutes);
            foreach (var user in usersInRoom)
            {
                _xabbot.ShowMessage($"Muting user '{user.Name}' for {minutes} minute(s)");
                AppendModerationNotification(user.Name, $"muted for {minutes} minute(s)");
            }
        }

        // Notify about users not in the room
        foreach (var userName in usersNotInRoom)
        {
            _xabbot.ShowMessage($"User '{userName}' not found");
            AppendModerationNotification(userName, "not found (mute)");
        }
    }

    private async Task BanSelectedUsersAsync(BanDuration duration)
    {
        if (ContextSelection is null)
            return;

        var durationText = duration switch
        {
            BanDuration.Hour => "for an hour",
            BanDuration.Day => "for a day",
            BanDuration.Permanent => "permanently",
            _ => ""
        };

        var usersInRoom = new List<IUser>();
        var usersNotInRoom = new List<string>();

        foreach (var message in ContextSelection)
        {
            if (_roomManager.Room is not null &&
                _roomManager.Room.TryGetUserByName(message.Name, out IUser? user))
            {
                usersInRoom.Add(user);
            }
            else
            {
                usersNotInRoom.Add(message.Name);
            }
        }

        // Ban users currently in the room
        if (usersInRoom.Count > 0)
        {
            await _moderation.BanUsersAsync(usersInRoom, duration);
            foreach (var user in usersInRoom)
            {
                _xabbot.ShowMessage($"Banned user '{user.Name}' {durationText}");
                AppendModerationNotification(user.Name, $"banned {durationText}");
            }
        }

        // Add users not in the room to the deferred ban list
        if (usersNotInRoom.Count > 0)
        {
            _moderationCommands ??= Locator.Current.GetService<IEnumerable<CommandModule>>()
                ?.OfType<ModerationCommands>()
                .FirstOrDefault();

            if (_moderationCommands is not null)
            {
                foreach (var userName in usersNotInRoom)
                {
                    _moderationCommands.AddToBanList(userName, duration);
                    _xabbot.ShowMessage($"User '{userName}' not found, will be banned {durationText} upon next entry to this room.");
                    AppendModerationNotification(userName, $"will be banned {durationText} upon next entry");
                }
            }
        }
    }

    private Task KickUserByNameAsync(string userName) => TryModerate(async () =>
    {
        if (_roomManager.Room?.TryGetUserByName(userName, out var user) == true)
        {
            await _moderation.KickUsersAsync([user]);
            _xabbot.ShowMessage($"Kicked user '{userName}'");
            AppendModerationNotification(userName, "kicked");
        }
        else
        {
            _xabbot.ShowMessage($"User '{userName}' not found");
            AppendModerationNotification(userName, "not found (kick)");
        }
    });

    private Task MuteUserByNameAsync((string Name, int Minutes) param) => TryModerate(async () =>
    {
        var (userName, minutes) = param;

        if (_roomManager.Room?.TryGetUserByName(userName, out var user) == true)
        {
            await _moderation.MuteUsersAsync([user], minutes);
            _xabbot.ShowMessage($"Muting user '{userName}' for {minutes} minute(s)");
            AppendModerationNotification(userName, $"muted for {minutes} minute(s)");
        }
        else
        {
            _xabbot.ShowMessage($"User '{userName}' not found");
            AppendModerationNotification(userName, "not found (mute)");
        }
    });

    private Task BanUserByNameAsync((string Name, BanDuration Duration) param) => TryModerate(async () =>
    {
        var (userName, duration) = param;

        var durationText = duration switch
        {
            BanDuration.Hour => "for an hour",
            BanDuration.Day => "for a day",
            BanDuration.Permanent => "permanently",
            _ => ""
        };

        if (_roomManager.Room?.TryGetUserByName(userName, out var user) == true)
        {
            await _moderation.BanUsersAsync([user], duration);
            _xabbot.ShowMessage($"Banned user '{userName}' {durationText}");
            AppendModerationNotification(userName, $"banned {durationText}");
        }
        else
        {
            // Add to deferred ban list
            _moderationCommands ??= Locator.Current.GetService<IEnumerable<CommandModule>>()
                ?.OfType<ModerationCommands>()
                .FirstOrDefault();

            if (_moderationCommands is not null)
            {
                _moderationCommands.AddToBanList(userName, duration);
                _xabbot.ShowMessage($"User '{userName}' not found, will be banned {durationText} upon next entry to this room.");
                AppendModerationNotification(userName, $"will be banned {durationText} upon next entry");
            }
        }
    });

    private async Task TryModerate(Func<Task> moderate)
    {
        try
        {
            await moderate();
        }
        catch (OperationInProgressException ex)
        {
            await _dialog.ShowAsync("Error", ex.Message);
        }
        catch (Exception ex)
        {
            await _dialog.ShowAsync("Error", ex.Message);
        }
    }
}