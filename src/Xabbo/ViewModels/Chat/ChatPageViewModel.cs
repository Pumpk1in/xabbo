using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Web;
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

    private readonly RoomManager _roomManager;
    private readonly ProfileManager _profileManager;
    private readonly RoomModerationController _moderation;
    private readonly TradeManager _tradeManager;
    private readonly WardrobePageViewModel _wardrobe;
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
    public ReactiveCommand<Unit, Unit> CopyUserToWardrobeCmd { get; }
    public ReactiveCommand<Unit, Unit> TradeUserCmd { get; }
    public ReactiveCommand<string, Unit> CopyUserFieldCmd { get; }
    public ReactiveCommand<string, Task> OpenUserProfileCmd { get; }

    public ReactiveCommand<string, Task> MuteUsersCmd { get; }
    public ReactiveCommand<Unit, Task> KickUsersCmd { get; }
    public ReactiveCommand<BanDuration, Task> BanUsersCmd { get; }
    public ReactiveCommand<Unit, Task> BounceUsersCmd { get; }

    public ReactiveCommand<Unit, Task> AddToProfanityFilterCmd { get; }
    public ReactiveCommand<string, Unit> RemoveFromProfanityFilterCmd { get; }

    public SelectionModel<ChatLogEntryViewModel> Selection { get; } = new SelectionModel<ChatLogEntryViewModel>()
    {
        SingleSelect = false
    };

    [Reactive] public string? FilterText { get; set; }
    [Reactive] public bool WhispersOnly { get; set; }

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
        RoomManager roomManager,
        ProfileManager profileManager,
        RoomModerationController moderation,
        TradeManager tradeManager,
        WardrobePageViewModel wardrobe,
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
        _roomManager = roomManager;
        _profileManager = profileManager;
        _moderation = moderation;
        _tradeManager = tradeManager;
        _wardrobe = wardrobe;
        _xabbot = xabbot;

        _roomManager.Entered += OnEnteredRoom;
        _roomManager.AvatarAdded += OnAvatarAdded;
        _roomManager.AvatarRemoved += OnAvatarRemoved;
        _roomManager.AvatarChat += RoomManager_AvatarChat;
        _roomManager.AvatarUpdated += RoomManager_AvatarUpdated;

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

        var canTradeUser = Observable.CombineLatest(
            hasSingleContextMessage,
            _tradeManager.WhenAnyValue(x => x.IsTrading).StartWith(false),
            this.WhenAnyValue(x => x.SelectedUserName).StartWith((string?)null),
            _profileManager.WhenAnyValue(x => x.UserData!.Name).StartWith((string?)null),
            (hasMessage, isTrading, selectedName, currentUserName) =>
                hasMessage && !isTrading && selectedName != null && selectedName != currentUserName
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
        CopyUserToWardrobeCmd = ReactiveCommand.Create(CopyUserToWardrobe, hasAnyContextMessage);
        TradeUserCmd = ReactiveCommand.Create(TradeUser, canTradeUser);
        CopyUserFieldCmd = ReactiveCommand.Create<string>(CopyUserField, hasSingleContextMessage);
        OpenUserProfileCmd = ReactiveCommand.Create<string, Task>(OpenUserProfile, hasSingleContextMessage);

        MuteUsersCmd = ReactiveCommand.Create<string, Task>(MuteUsersAsync, canMuteUsers);
        KickUsersCmd = ReactiveCommand.Create<Task>(KickUsersAsync, canKickUsers);
        BanUsersCmd = ReactiveCommand.Create<BanDuration, Task>(BanUsersAsync, canBanUsers);
        BounceUsersCmd = ReactiveCommand.Create<Task>(BounceUsersAsync, canBounceUsers);

        AddToProfanityFilterCmd = ReactiveCommand.Create<Task>(AddToProfanityFilterAsync, hasSingleContextMessage);
        RemoveFromProfanityFilterCmd = ReactiveCommand.Create<string>(RemoveFromProfanityFilter);

        // Filter by text search and whispers only
        var filterPredicate = this
            .WhenAnyValue(x => x.FilterText, x => x.WhispersOnly)
            .Select(tuple => CreateCombinedFilter(tuple.Item1, tuple.Item2));

        _cache
            .Connect()
            .Filter(filterPredicate)
            .ObserveOn(RxApp.MainThreadScheduler)
            .SortAndBind(out _messages, SortExpressionComparer<ChatLogEntryViewModel>.Ascending(x => x.EntryId))
            .Subscribe();

        CopySelectedEntriesCmd = ReactiveCommand.Create(CopySelectedEntries);
    }

    // Combined filter for text search and whispers only
    private Func<ChatLogEntryViewModel, bool> CreateCombinedFilter(string? filterText, bool whispersOnly)
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
                    action.UserName.Contains(keyword, StringComparison.OrdinalIgnoreCase)),
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

    private void OnAvatarAdded(AvatarEventArgs e)
    {
        if (_roomManager.IsLoadingRoom || !Config.UserEntry || e.Avatar.Type is not AvatarType.User)
            return;

        AppendLog(new ChatLogAvatarActionViewModel
        {
            UserName = e.Avatar.Name,
            Action = "entered the room"
        });
    }

    private void OnAvatarRemoved(AvatarEventArgs e)
    {
        if (_roomManager.IsLoadingRoom || !Config.UserEntry || e.Avatar.Type is not AvatarType.User)
            return;

        AppendLog(new ChatLogAvatarActionViewModel
        {
            UserName = e.Avatar.Name.Equals(_profileManager.UserData?.Name) ? "You" : e.Avatar.Name,
            Action = "left the room"
        });
    }

    private void RoomManager_AvatarUpdated(AvatarEventArgs e)
    {
        if (Config.Trades &&
            e.Avatar.PreviousUpdate is { IsTrading: bool wasTrading } &&
            e.Avatar.CurrentUpdate is { IsTrading: bool isTrading } &&
            isTrading != wasTrading)
        {
            if (isTrading)
            {
                AppendLog(new ChatLogAvatarActionViewModel
                {
                    UserName = e.Avatar.Name,
                    Action = "started trading"
                });
            }
            else
            {
                AppendLog(new ChatLogAvatarActionViewModel
                {
                    UserName = e.Avatar.Name,
                    Action = "stopped trading"
                });
            }
        }
    }

    private void OnEnteredRoom(RoomEventArgs e) => AppendLog(new ChatLogRoomEntryViewModel
    {
        RoomName = e.Room.Data?.Name ?? "?",
        RoomOwner = e.Room.Data?.OwnerName ?? "?"
    });

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

        AppendLog(new ChatMessageViewModel
        {
            Type = e.ChatType,
            BubbleStyle = e.BubbleStyle,
            Name = e.Avatar.Name,
            Message = renderedMessage,
            FigureString = figureString,
            HasProfanity = hasProfanity,
            MessageSegments = segments,
            MatchedWords = matchedWords,
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

    private void TradeUser()
    {
        if (ContextSelection is not [var message])
            return;

        if (_roomManager.Room?.TryGetUserByName(message.Name, out var user) == true)
        {
            _ext.Send(new TradeUserMsg(user.Index));
        }
    }

    private void CopyUserToWardrobe()
    {
        if (ContextSelection is { } selection)
        {
            foreach (var message in selection)
            {
                if (!string.IsNullOrEmpty(message.FigureString))
                {
                    if (_roomManager.Room?.TryGetUserByName(message.Name, out var user) == true)
                    {
                        _wardrobe.AddFigure(user.Gender, user.Figure);
                    }
                }
            }
        }
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
    private Task KickUsersAsync() => TryModerate(() => _moderation.KickUsersAsync(GetSelectedUsers()));
    private Task BanUsersAsync(BanDuration duration) => TryModerate(() => BanSelectedUsersAsync(duration));
    private Task BounceUsersAsync() => TryModerate(() => _moderation.BounceUsersAsync(GetSelectedUsers()));

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
            }
        }

        // Notify about users not in the room
        foreach (var userName in usersNotInRoom)
        {
            _xabbot.ShowMessage($"User '{userName}' not found");
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
                }
            }
        }
    }

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