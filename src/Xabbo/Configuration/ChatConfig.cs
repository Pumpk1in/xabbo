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
    // Enable each sanction type independently (both on by default).
    [Reactive] public bool VoteBanEnabled { get; set; } = true;
    [Reactive] public bool VoteMuteEnabled { get; set; } = true;
    // Bubble style forced on all automated vote messages (whispers + announcements), independent of the global chat bubble.
    [Reactive] public int VoteBubbleStyle { get; set; } = 25;
    [Reactive] public bool VoteWhisperFeedback { get; set; } = true;
    [Reactive] public bool VoteAnnounceSanction { get; set; } = false;
    // Announce in room chat when a vote starts (the first vote on a target), explaining how to vote.
    [Reactive] public bool VoteAnnounceStart { get; set; } = false;
    [Reactive] public int VoteMinPresenceMinutes { get; set; } = 3;
    // Minimum "for" votes required, per sanction type (a ban demands more people than a mute).
    [Reactive] public int VoteBanQuorum { get; set; } = 4;
    [Reactive] public int VoteMuteQuorum { get; set; } = 2;
    // Shared consensus bar: for / (for + against) must reach this percentage.
    [Reactive] public int VoteApprovalPercent { get; set; } = 66;
    [Reactive] public int VoteSessionTtlMinutes { get; set; } = 5;
    [Reactive] public int VoteCooldownMinutes { get; set; } = 15;
    // Grace delay: once the threshold is reached, wait this many seconds of vote silence before
    // applying the sanction, so a last-second counter-vote can still cancel it. 0 = apply instantly.
    [Reactive] public int VoteGraceSeconds { get; set; } = 8;

    // Whisper templates sent to voters. Placeholders: {name}, {for}, {against}.
    // Help fragments assembled per enabled sanction type (ban / mute) + the always-shown shorthand,
    // so the whisper never mentions a vote type that is disabled.
    [Reactive] public string VoteHelpBanText { get; set; } =
        "[Bannir] :voteban <pseudo>";
    [Reactive] public string VoteHelpMuteText { get; set; } =
        "[Muter] :votemute <pseudo>";
    [Reactive] public string VoteHelpShorthandText { get; set; } =
        "[Participer au vote] :vote yes / :vote no.";
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
    [Reactive] public string VoteInProgressText { get; set; } =
        "Un vote est déjà en cours, attends qu'il se termine avant d'en lancer un autre.";
    // Lead-in only — the enabled start verbs are appended by the controller.
    [Reactive] public string VoteNoActiveText { get; set; } =
        "Aucun vote n'est en cours pour le moment.";
    [Reactive] public string VoteNotPresentLongEnoughText { get; set; } =
        "Tu viens d'arriver, reste un peu dans la pièce avant de pouvoir voter.";

    // Public room announcements when a vote passes (sent from your own avatar, not a whisper).
    [Reactive] public string VoteAnnounceBanText { get; set; } =
        "{name} a été banni 1h suite au vote de la communauté ({for} pour / {against} contre).";
    [Reactive] public string VoteAnnounceMuteText { get; set; } =
        "{name} a été muté 10 min suite au vote de la communauté ({for} pour / {against} contre).";

    // Public room announcement when a vote starts (first vote on a target), explaining how to join in.
    [Reactive] public string VoteStartBanText { get; set; } =
        "Un vote pour bannir {name} vient de démarrer ! Tape :vote yes pour, ou :vote no contre.";
    [Reactive] public string VoteStartMuteText { get; set; } =
        "Un vote pour muter {name} vient de démarrer ! Tape :vote yes pour, ou :vote no contre.";
}