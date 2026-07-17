using System.Linq;
using System.Reactive.Linq;
using Splat;
using ReactiveUI;
using HanumanInstitute.MvvmDialogs;
using FluentIcons.Common;
using FluentIcons.Avalonia.Fluent;
using IconSource = FluentAvalonia.UI.Controls.IconSource;

using Xabbo.Extension;
using Xabbo.Components;
using Xabbo.Configuration;
using Xabbo.Services.Abstractions;
using Xabbo.Controllers;
using Xabbo.Models;

namespace Xabbo.ViewModels;

public class GeneralPageViewModel : PageViewModel
{
    public override string Header => "General";
    public override IconSource? Icon { get; } = new SymbolIconSource { Symbol = Symbol.AppGeneric };

    private readonly IConfigProvider<AppConfig> _settingsProvider;
    private readonly IExtension _ext;

    private readonly ObservableAsPropertyHelper<AppConfig> _config;
    public AppConfig Config => _config.Value;

    public GeneralPageViewModel(IConfigProvider<AppConfig> settingsProvider,
        IAppPathProvider appPathProvider,
        IDialogService dialogService,
        IExtension ext)
    {
        _settingsProvider = settingsProvider;
        _ext = ext;

        _config = _settingsProvider
            .WhenAnyValue(x => x.Value)
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToProperty(this, x => x.Config);

        // Vote bubble picker: reflect VoteBubbleStyle into the selection (init + on reload)...
        _settingsProvider.WhenAnyValue(x => x.Value)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(cfg => SelectedVoteBubble =
                ChatBubbleOption.NormalBubbles.FirstOrDefault(b => b.Id == cfg.Chat.VoteBubbleStyle)
                ?? ChatBubbleOption.NormalBubbles[0]);
        // ...and write the selection back to config.
        this.WhenAnyValue(x => x.SelectedVoteBubble)
            .WhereNotNull()
            .Subscribe(b => Config.Chat.VoteBubbleStyle = b.Id);
    }

    [Reactive] public ChatBubbleOption? SelectedVoteBubble { get; set; }

    [Reactive] public bool IsRoomExpanded { get; set; } = true;
    [Reactive] public bool IsMovementExpanded { get; set; } = true;
    [Reactive] public bool IsClickToExpanded { get; set; } = true;
    [Reactive] public bool IsEntryExpanded { get; set; } = true;
    [Reactive] public bool IsChatExpanded { get; set; } = true;
    [Reactive] public bool IsMuteExpanded { get; set; } = true;
    [Reactive] public bool IsAntiSpamExpanded { get; set; } = true;
    [Reactive] public bool IsVoteModerationExpanded { get; set; } = true;
    [Reactive] public bool IsFurniExpanded { get; set; } = true;
    [Reactive] public bool IsHandItemExpanded { get; set; } = true;
    [Reactive] public bool IsAlertsExpanded { get; set; } = true;
    [Reactive] public bool IsFlashWindowExpanded { get; set; } = true;
    [Reactive] public bool IsMiscExpanded { get; set; } = true;

    [DependencyInjectionProperty] public RoomAvatarsController? Avatars { get; set; }
    [DependencyInjectionProperty] public ChatComponent? ChatComponent { get; set; }
    [DependencyInjectionProperty] public FurniActionsComponent? Furni { get; set; }
    [DependencyInjectionProperty] public AntiHandItemComponent? HandItem { get; set; }
    [DependencyInjectionProperty] public AntiIdleComponent? AntiIdle { get; set; }
    [DependencyInjectionProperty] public AntiTradeComponent? AntiTrade { get; set; }
    [DependencyInjectionProperty] public AntiTurnComponent? AntiTurn { get; set; }
    [DependencyInjectionProperty] public AntiTypingComponent? AntiTyping { get; set; }
    [DependencyInjectionProperty] public AntiWalkComponent? AntiWalk { get; set; }
    [DependencyInjectionProperty] public ClickThroughComponent? ClickThrough { get; set; }
    [DependencyInjectionProperty] public ClickToController? ClickTo { get; set; }
    [DependencyInjectionProperty] public RespectedComponent? Respected { get; set; }
    [DependencyInjectionProperty] public RoomEntryComponent? RoomEntry { get; set; }
    [DependencyInjectionProperty] public DoorbellComponent? Doorbell { get; set; }
    [DependencyInjectionProperty] public FlattenRoomComponent? Flatten { get; set; }
    [DependencyInjectionProperty] public AvatarOverlayComponent? Overlay { get; set; }
    [DependencyInjectionProperty] public AntiHcGiftNotificationComponent? AntiHcNotification { get; set; }
    [DependencyInjectionProperty] public LightingComponent? Lighting { get; set; }
}
