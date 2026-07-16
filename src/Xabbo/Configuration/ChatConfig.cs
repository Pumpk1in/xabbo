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

    [Reactive] public bool VoteModeration { get; set; } = false;
    [Reactive] public bool VoteWhisperFeedback { get; set; } = true;
    [Reactive] public int VoteMinMessages { get; set; } = 2;
    [Reactive] public int VoteNetThreshold { get; set; } = 4;
    [Reactive] public int VoteQuorum { get; set; } = 3;
    [Reactive] public int VoteSessionTtlMinutes { get; set; } = 5;
    [Reactive] public int VoteCooldownMinutes { get; set; } = 15;

    // Whisper templates sent to voters. Placeholders: {name}, {for}, {against}.
    [Reactive] public string VoteHelpText { get; set; } =
        "Modération par vote : /voteban <pseudo> (bannir), /votemute <pseudo> (muter). Pour défendre : /votekeep <pseudo>, /voteunmute <pseudo>.";
    [Reactive] public string VoteCountedText { get; set; } =
        "Ton vote concernant {name} est pris en compte ({for} pour / {against} contre).";
    [Reactive] public string VoteChangedText { get; set; } =
        "Ton vote sur {name} a été changé ({for} pour / {against} contre).";
    [Reactive] public string VoteAlreadyText { get; set; } =
        "Tu as déjà voté, pas besoin d'insister.";
    [Reactive] public string VoteWhitelistedText { get; set; } =
        "{name} est un joueur protégé, impossible de le cibler.";
    [Reactive] public string VoteCooldownText { get; set; } =
        "{name} a déjà été sanctionné récemment, patiente un peu.";
    [Reactive] public string VoteImmuneText { get; set; } =
        "{name} est protégé pour le moment.";
    [Reactive] public string VoteSelfText { get; set; } =
        "Tu ne peux pas voter contre toi-même.";
    [Reactive] public string VoteNotAllowedText { get; set; } =
        "{name} ne peut pas être ciblé.";
}