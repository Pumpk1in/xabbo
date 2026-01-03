using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Web;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DynamicData;
using DynamicData.Binding;
using DynamicData.Kernel;
using ReactiveUI;
using Splat;

using Xabbo.Extension;
using Xabbo.Core;
using Xabbo.Core.Events;
using Xabbo.Core.Game;
using Xabbo.Core.Messages.Outgoing;
using Xabbo.Services.Abstractions;
using Xabbo.Controllers;
using Xabbo.Command;
using Xabbo.Command.Modules;
using Xabbo.Components;

namespace Xabbo.ViewModels;

public class RoomVisitorsViewModel : ViewModelBase
{
    private readonly IExtension _ext;
    private readonly Microsoft.Extensions.Logging.ILogger _logger;
    private readonly IUiContext _context;
    private readonly ILauncherService _launcher;
    private readonly ProfileManager _profileManager;
    private readonly RoomManager _roomManager;
    private readonly RoomModerationController _moderation;
    private readonly XabbotComponent _xabbot;
    private ModerationCommands? _moderationCommands;

    private readonly ReadOnlyObservableCollection<VisitorViewModel> _visitors;
    public ReadOnlyObservableCollection<VisitorViewModel> Visitors => _visitors;

    private readonly SourceCache<VisitorViewModel, string> _visitorCache = new(x => x.Name);

    [Reactive] public bool IsAvailable { get; set; }
    [Reactive] public string FilterText { get; set; } = "";
    [Reactive] public VisitorViewModel? ContextSelection { get; set; }

    public ReactiveCommand<string, Task> OpenUserProfileCmd { get; }
    public ReactiveCommand<BanDuration, Task> BanVisitorCmd { get; }

    public RoomVisitorsViewModel(
        IHostApplicationLifetime lifetime,
        ILoggerFactory loggerFactory,
        IUiContext context,
        IExtension extension,
        ILauncherService launcher,
        ProfileManager profileManager,
        RoomManager roomManager,
        RoomModerationController moderation,
        XabbotComponent xabbot)
    {
        _logger = loggerFactory.CreateLogger<RoomVisitorsViewModel>();
        _context = context;
        _ext = extension;
        _launcher = launcher;
        _profileManager = profileManager;
        _roomManager = roomManager ?? throw new ArgumentNullException(nameof(roomManager));
        _moderation = moderation;
        _xabbot = xabbot;

        var propertyChanges = _visitorCache.Connect()
            .WhenPropertyChanged(x => x.Index)
            .Throttle(TimeSpan.FromMilliseconds(250))
            .Select(_ => Unit.Default);

        _visitorCache.Connect()
            .Filter(this.WhenAnyValue(x => x.FilterText, CreateFilter))
            .ObserveOn(RxApp.MainThreadScheduler)
            .SortAndBind(out _visitors, SortExpressionComparer<VisitorViewModel>.Descending(x => x.Index))
            .Subscribe();

        var hasContextSelection = this
            .WhenAnyValue(x => x.ContextSelection)
            .Select(x => x is not null)
            .ObserveOn(RxApp.MainThreadScheduler);

        OpenUserProfileCmd = ReactiveCommand.Create<string, Task>(OpenUserProfile, hasContextSelection);

        BanVisitorCmd = ReactiveCommand.Create<BanDuration, Task>(
            BanVisitorAsync,
            Observable.CombineLatest(
                this.WhenAnyValue(x => x.ContextSelection),
                profileManager.WhenAnyValue(x => x.UserData),
                moderation.WhenAnyValue(x => x.CanBan),
                (selection, userData, canBan) =>
                {
                    if (selection is null || !canBan)
                        return false;

                    // Don't allow banning yourself
                    if (selection.Name == userData?.Name)
                        return false;

                    return true;
                }
            )
            .ObserveOn(RxApp.MainThreadScheduler)
        );

        lifetime.ApplicationStarted.Register(() => Task.Run(InitializeAsync));
    }

    private Func<VisitorViewModel, bool> CreateFilter(string filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText))
            return static (vm) => true;

        return (vm) => vm.Name.Contains(filterText, StringComparison.CurrentCultureIgnoreCase);
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _profileManager.GetUserDataAsync();
        }
        catch { return; }

        _ext.Disconnected += OnGameDisconnected;

        _roomManager.Left += OnLeftRoom;
        _roomManager.AvatarsAdded += OnAvatarsAdded;
        _roomManager.AvatarRemoved += OnAvatarsRemoved;

        IsAvailable = true;
    }

    private void ClearVisitors() => _visitorCache.Clear();

    private void OnGameDisconnected() => ClearVisitors();

    private void OnLeftRoom() => ClearVisitors();

    private void OnAvatarsAdded(AvatarsEventArgs e)
    {
        if (!_roomManager.IsLoadingRoom && !_roomManager.IsInRoom)
            return;

        var newLogs = new List<VisitorViewModel>();
        var now = DateTime.Now;

        _visitorCache.Edit(cache => {
            foreach (var user in e.Avatars.OfType<IUser>())
            {
                cache
                    .Lookup(user.Name)
                    .IfHasValue(vm => {
                        vm.Visits++;
                        vm.Index = user.Index;
                        vm.Entered = now;
                    })
                    .Else(() => {
                        var visitorLog = new VisitorViewModel(user.Index, user.Id, user.Name);
                        if (_roomManager.IsLoadingRoom)
                        {
                            if (user.Name == _profileManager.UserData?.Name)
                                visitorLog.Entered = now;
                        }
                        else
                        {
                            visitorLog.Entered = now;
                        }
                        cache.AddOrUpdate(visitorLog);
                    });
            }
            cache.Refresh();
        });
    }

    private void OnAvatarsRemoved(AvatarEventArgs e)
    {
        if (e.Avatar.Type is not AvatarType.User)
            return;

        _visitorCache
            .Lookup(e.Avatar.Name)
            .IfHasValue(vm => {
                vm.Left = DateTime.Now;
            });
    }

    private async Task OpenUserProfile(string type)
    {
        if (ContextSelection is null)
            return;

        switch (type)
        {
            case "game":
                var profile = await _ext.RequestAsync(new GetProfileByNameMsg(ContextSelection.Name), block: false);
                if (!profile.DisplayInClient)
                {
                    _logger.LogWarning($"Profile for '{ContextSelection.Name}' is not visible.");
                }
                break;
            case "web":
                _launcher.Launch($"https://{_ext.Session.Hotel.WebHost}/profile/{HttpUtility.UrlEncode(ContextSelection.Name)}");
                break;
            case "habbowidgets":
                _launcher.Launch($"https://www.habbowidgets.com/habinfo/{_ext.Session.Hotel.Domain}/{HttpUtility.UrlEncode(ContextSelection.Name)}");
                break;
        }
    }

    private async Task BanVisitorAsync(BanDuration duration)
    {
        if (ContextSelection is null)
            return;

        var visitor = ContextSelection;
        var durationText = duration switch
        {
            BanDuration.Hour => "for an hour",
            BanDuration.Day => "for a day",
            BanDuration.Permanent => "permanently",
            _ => ""
        };

        // Check if the user is currently in the room
        if (_roomManager.Room is not null &&
            _roomManager.Room.TryGetUserByName(visitor.Name, out IUser? user))
        {
            // User is in the room - ban immediately
            await _moderation.BanUsersAsync([user], duration);
            _xabbot.ShowMessage($"Banned user '{visitor.Name}' {durationText}");
        }
        else
        {
            // User is not in the room - add to ban list for deferred ban
            // Lazy-load ModerationCommands to avoid circular dependency
            _moderationCommands ??= Locator.Current.GetService<IEnumerable<CommandModule>>()
                ?.OfType<ModerationCommands>()
                .FirstOrDefault();

            if (_moderationCommands is not null)
            {
                _moderationCommands.AddToBanList(visitor.Name, duration);
                _xabbot.ShowMessage($"User '{visitor.Name}' not found, will be banned {durationText} upon next entry to this room.");
                _logger.LogInformation($"User '{visitor.Name}' added to ban list. Will be banned on next entry.");
            }
            else
            {
                _xabbot.ShowMessage($"Error: ModerationCommands module not found.");
                _logger.LogError("ModerationCommands module not found. Cannot add user to ban list.");
            }
        }
    }
}
