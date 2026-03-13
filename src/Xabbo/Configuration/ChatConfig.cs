using ReactiveUI;
using Xabbo.Core;

namespace Xabbo.Configuration;

public sealed class ChatConfig : ReactiveObject
{
    [Reactive] public bool AlwaysShout { get; set; }
    [Reactive] public bool MuteAll { get; set; }
    [Reactive] public bool MutePets { get; set; } = true;
    [Reactive] public bool MutePetCommands { get; set; } = true;
    [Reactive] public bool MuteBots { get; set; } = true;
    [Reactive] public bool MuteWired { get; set; }
    [Reactive] public bool MuteRespects { get; set; }
    [Reactive] public bool MuteScratches { get; set; }
    [Reactive] public int BubbleStyle { get; set; } = 0;
    [Reactive] public ChatLogConfig Log { get; set; } = new();
    [Reactive] public ProfanityConfig Profanity { get; set; } = new();

    [Reactive] public bool AntiSpam { get; set; } = true;
    [Reactive] public int AntiSpamThreshold { get; set; } = 5;
    [Reactive] public int AntiSpamWindowSeconds { get; set; } = 3;
    [Reactive] public BanDuration AntiSpamBanDuration { get; set; } = BanDuration.Hour;
}