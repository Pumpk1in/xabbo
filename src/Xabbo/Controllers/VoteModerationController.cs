using System.Text.Json;
using Splat;
using Xabbo.Configuration;
using Xabbo.Core;
using Xabbo.Core.Events;
using Xabbo.Core.Game;
using Xabbo.Core.Messages.Outgoing;
using Xabbo.Extension;
using Xabbo.Messages;
using Xabbo.Models;
using Xabbo.Models.Enums;
using Xabbo.Serialization;
using Xabbo.Services.Abstractions;
using Xabbo.ViewModels;

namespace Xabbo.Controllers;

public enum VoteType { Ban, Mute }

/// <summary>
/// Community vote-ban / vote-mute engine. Players type <c>:voteban</c>, <c>:votenoban</c>,
/// <c>:votemute</c> or <c>:votenomute</c> in room chat; when a net threshold + quorum is reached
/// the target is banned 1h or muted 10min. Runs entirely off incoming chat
/// (<see cref="RoomManager.AvatarChat"/>) — the moderator is alerted only through the UI.
/// </summary>
[Intercept]
public partial class VoteModerationController : ControllerBase
{
    private enum VoteDirection { For, Against }

    private const int MuteMinutes = 10; // 10min = the max mute a moderator can set from the game client,
                                        // so a vote-mute can never shorten an existing manual mute.
    private static readonly TimeSpan ImmunityDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan DeferredSanctionWindow = TimeSpan.FromMinutes(15);

    private sealed class VoteSession
    {
        public required VoteType Type { get; init; }
        public Dictionary<Id, VoteDirection> Votes { get; } = [];
        public Dictionary<Id, string> VoterNames { get; } = [];
        public HashSet<Id> WarnedDuplicate { get; } = [];
        public DateTimeOffset LastVoteTime { get; set; }

        public int For => Votes.Values.Count(v => v == VoteDirection.For);
        public int Against => Votes.Values.Count(v => v == VoteDirection.Against);

        /// <summary>Resolves the current voters into (for, against) name lists.</summary>
        public (List<string> For, List<string> Against) VoterLists()
        {
            List<string> forList = [], againstList = [];
            foreach (var (id, direction) in Votes)
            {
                var name = VoterNames.GetValueOrDefault(id) ?? id.ToString();
                (direction == VoteDirection.For ? forList : againstList).Add(name);
            }
            return (forList, againstList);
        }
    }

    private readonly record struct PendingSanction(
        DateTimeOffset Expiry, List<string> ForVoters, List<string> AgainstVoters);

    private readonly IConfigProvider<AppConfig> _config;
    private readonly RoomManager _roomManager;
    private readonly RoomModerationController _moderation;
    private readonly IAppPathProvider _appPathProvider;

    private readonly object _lock = new();
    private readonly Dictionary<(string Name, VoteType Type), VoteSession> _sessions = [];
    private readonly Dictionary<Id, int> _messageCounts = [];
    private readonly Dictionary<(string Name, VoteType Type), PendingSanction> _pendingSanctions = [];
    private readonly Dictionary<(string Name, VoteType Type), DateTimeOffset> _cooldownUntil = [];
    private readonly Dictionary<Id, DateTimeOffset> _immuneBanUntil = [];
    private readonly Dictionary<Id, DateTimeOffset> _immuneMuteUntil = [];
    private readonly Dictionary<Id, DateTimeOffset> _lastRejectWhisper = [];

    private VoteWhitelistData _whitelist = new();
    private readonly HashSet<long> _whitelistIds = [];
    private readonly HashSet<string> _whitelistNames = new(StringComparer.OrdinalIgnoreCase);

    private ChatPageViewModel? _chatPage;

    private AppConfig Settings => _config.Value;

    public VoteModerationController(
        IExtension extension,
        IConfigProvider<AppConfig> config,
        RoomManager roomManager,
        RoomModerationController moderation,
        IAppPathProvider appPathProvider)
        : base(extension)
    {
        _config = config;
        _roomManager = roomManager;
        _moderation = moderation;
        _appPathProvider = appPathProvider;

        _roomManager.AvatarChat += OnAvatarChat;
        _roomManager.AvatarsAdded += OnAvatarsAdded;
        _roomManager.Left += OnLeftRoom;

        LoadWhitelist();
    }

    private static bool TryParseVerb(string verb, out VoteType type, out VoteDirection direction)
    {
        switch (verb)
        {
            case "voteban": type = VoteType.Ban; direction = VoteDirection.For; return true;
            case "votenoban": type = VoteType.Ban; direction = VoteDirection.Against; return true;
            case "votemute": type = VoteType.Mute; direction = VoteDirection.For; return true;
            case "votenomute": type = VoteType.Mute; direction = VoteDirection.Against; return true;
            default: type = default; direction = default; return false;
        }
    }

    private void OnAvatarChat(AvatarChatEventArgs e)
    {
        if (!Settings.Chat.VoteModeration) return;
        if (e.Avatar.Type != AvatarType.User || e.Avatar is not IUser voter) return;
        if (e.ChatType == ChatType.Whisper) return;

        var message = e.Message?.Trim();
        if (string.IsNullOrEmpty(message)) return;

        var parts = message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var first = parts[0];

        // A vote command must be the whole message, typed as ":verb <pseudo>": it must start
        // with the colon-prefixed verb, so it never fires from inside a sentence. The colon
        // (not the slash) lets the moderator vote too — Xabbo's CommandManager blocks outgoing
        // "/"-prefixed chat, but ":"-prefixed chat passes through to the server as normal chat.
        if (!first.StartsWith(':') ||
            !TryParseVerb(first[1..].ToLowerInvariant(), out var type, out var direction))
        {
            // Only genuine (non-command) public chat counts toward voter eligibility.
            lock (_lock)
                _messageCounts[voter.Id] = _messageCounts.GetValueOrDefault(voter.Id) + 1;
            return;
        }

        // Each sanction type can be enabled independently.
        if (type == VoteType.Ban ? !Settings.Chat.VoteBanEnabled : !Settings.Chat.VoteMuteEnabled)
            return;

        // Require exactly "/verb <pseudo>" — a bare command or any extra words is not a vote.
        if (parts.Length != 2)
        {
            WhisperReject(voter, Settings.Chat.VoteHelpText);
            return;
        }

        var targetName = parts[1];
        HandleVote(voter, targetName, type, direction);
    }

    private static string Format(string template, string name, int forCount, int againstCount) => template
        .Replace("{name}", name)
        .Replace("{for}", forCount.ToString())
        .Replace("{against}", againstCount.ToString());

    private void HandleVote(IUser voter, string targetName, VoteType type, VoteDirection direction)
    {
        var room = _roomManager.Room;
        if (room is null) return;

        // No self-voting.
        if (voter.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
        {
            WhisperReject(voter, Format(Settings.Chat.VoteSelfText, voter.Name, 0, 0));
            return;
        }

        var nameLower = targetName.ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        IUser? target = room.TryGetUserByName(targetName, out var u) ? u : null;
        var display = target?.Name ?? targetName;

        string? infoWhisper = null;
        string? rejectWhisper = null;
        bool announceStart = false;
        (IUser Target, VoteType Type, List<string> ForVoters, List<string> AgainstVoters)? toApply = null;

        lock (_lock)
        {
            if (IsWhitelisted(target, targetName))
            {
                rejectWhisper = Format(Settings.Chat.VoteWhitelistedText, display, 0, 0);
            }
            else if (voter.RightsLevel < RightsLevel.Standard &&
                     _messageCounts.GetValueOrDefault(voter.Id) < Settings.Chat.VoteMinMessages)
            {
                // Eligibility rule is intentionally never revealed — stay completely silent.
                // Players with room rights (Standard rights, group admins, owners) are trusted
                // and exempt from the min-messages gate, so they can vote right away.
                return;
            }
            else if (_cooldownUntil.TryGetValue((nameLower, type), out var cd) && cd > now)
            {
                rejectWhisper = Format(Settings.Chat.VoteCooldownText, display, 0, 0);
            }
            else if (target is not null &&
                     (type == VoteType.Ban ? _immuneBanUntil : _immuneMuteUntil)
                         .TryGetValue(target.Id, out var im) && im > now)
            {
                rejectWhisper = Format(Settings.Chat.VoteImmuneText, display, 0, 0);
            }
            else if (target is not null &&
                     (IsVoteProtected(target) || !_moderation.CanModerate(ToModerationType(type), target)))
            {
                rejectWhisper = Format(Settings.Chat.VoteNotAllowedText, display, 0, 0);
            }
            else
            {
                var key = (nameLower, type);
                bool sessionActive = _sessions.TryGetValue(key, out var session) &&
                    (now - session.LastVoteTime).TotalMinutes <= Settings.Chat.VoteSessionTtlMinutes;

                if (!sessionActive && Settings.Chat.VoteSingleActive && HasOtherActiveVote(key, now))
                {
                    // Anti-spam: only one vote may run at a time until it's applied or expires.
                    rejectWhisper = Format(Settings.Chat.VoteInProgressText, display, 0, 0);
                }
                else
                {
                    if (!sessionActive)
                    {
                        session = new VoteSession { Type = type };
                        _sessions[key] = session;
                    }

                    bool alreadyVoted = session!.Votes.TryGetValue(voter.Id, out var existing);
                    if (alreadyVoted && existing == direction)
                    {
                        // Duplicate vote — warn once, then stay silent so we don't spam back.
                        if (session.WarnedDuplicate.Add(voter.Id))
                            rejectWhisper = Format(Settings.Chat.VoteAlreadyText, display, session.For, session.Against);
                    }
                    else
                    {
                        // The very first vote in a fresh session "starts" the vote → optional public announce.
                        announceStart = session.Votes.Count == 0;
                        session.Votes[voter.Id] = direction;
                        session.VoterNames[voter.Id] = voter.Name;
                        session.WarnedDuplicate.Remove(voter.Id);
                        session.LastVoteTime = now;

                        infoWhisper = Format(
                            alreadyVoted ? Settings.Chat.VoteChangedText : Settings.Chat.VoteCountedText,
                            display, session.For, session.Against);

                        if (VotePassed(type, session.For, session.Against))
                        {
                            var cooldown = now + TimeSpan.FromMinutes(Settings.Chat.VoteCooldownMinutes);
                            var (forVoters, againstVoters) = session.VoterLists();

                            if (target is not null && (type == VoteType.Ban ? _moderation.CanBan : _moderation.CanMute))
                            {
                                _sessions.Remove(key);
                                _cooldownUntil[key] = cooldown;
                                toApply = (target, type, forVoters, againstVoters);
                            }
                            else if (target is null)
                            {
                                // Target isn't in the room (fled or stepped out) — apply on return, within the window.
                                _sessions.Remove(key);
                                _cooldownUntil[key] = cooldown;
                                _pendingSanctions[key] = new PendingSanction(now + DeferredSanctionWindow, forVoters, againstVoters);
                            }
                        }
                    }
                }
            }
        }

        if (rejectWhisper is not null) WhisperReject(voter, rejectWhisper);
        else if (infoWhisper is not null) Whisper(voter, infoWhisper);

        if (announceStart) AnnounceStart(display, type);

        if (toApply is { } app)
            _ = ApplySanctionAsync(app.Target, app.Type, app.ForVoters, app.AgainstVoters);
    }

    private void AnnounceStart(string targetName, VoteType type)
    {
        if (!Settings.Chat.VoteAnnounceStart) return;

        var template = type == VoteType.Ban
            ? Settings.Chat.VoteStartBanText
            : Settings.Chat.VoteStartMuteText;
        var text = Format(template, targetName, 0, 0);
        if (!string.IsNullOrWhiteSpace(text))
            Ext.Send(new ChatMsg(ChatType.Talk, text, Settings.Chat.VoteBubbleStyle));
    }

    /// <summary>
    /// A vote passes when the "for" count reaches the per-type quorum AND the approval
    /// ratio (for / total votes) reaches the shared percentage. The ratio replaces the old
    /// absolute net margin so passing reflects genuine consensus, not just a raw lead.
    /// </summary>
    private bool VotePassed(VoteType type, int forCount, int againstCount)
    {
        int quorum = type == VoteType.Ban ? Settings.Chat.VoteBanQuorum : Settings.Chat.VoteMuteQuorum;
        int total = forCount + againstCount;
        return forCount >= quorum && forCount * 100 >= total * Settings.Chat.VoteApprovalPercent;
    }

    /// <summary>True if any vote other than <paramref name="exceptKey"/> is still within its TTL (anti-spam).</summary>
    private bool HasOtherActiveVote((string Name, VoteType Type) exceptKey, DateTimeOffset now)
    {
        double ttl = Settings.Chat.VoteSessionTtlMinutes;
        foreach (var (k, session) in _sessions)
        {
            if (k == exceptKey) continue;
            if ((now - session.LastVoteTime).TotalMinutes <= ttl) return true;
        }
        return false;
    }

    private void Whisper(IUser voter, string message)
    {
        if (!Settings.Chat.VoteWhisperFeedback) return;
        Ext.Send(new WhisperMsg(voter.Name, message, Settings.Chat.VoteBubbleStyle));
    }

    private void WhisperReject(IUser voter, string message)
    {
        if (!Settings.Chat.VoteWhisperFeedback) return;

        var now = DateTimeOffset.UtcNow;
        lock (_lock)
        {
            // Throttle rejection whispers so a spamming voter can't make us spam back.
            if (_lastRejectWhisper.TryGetValue(voter.Id, out var last) && (now - last).TotalSeconds < 3)
                return;
            _lastRejectWhisper[voter.Id] = now;
        }

        Ext.Send(new WhisperMsg(voter.Name, message, Settings.Chat.VoteBubbleStyle));
    }

    private async Task ApplySanctionAsync(
        IUser target, VoteType type, IReadOnlyList<string> forVoters, IReadOnlyList<string> againstVoters)
    {
        if (type == VoteType.Ban)
            await _moderation.BanUsersAsync([target], BanDuration.Hour);
        else
            await _moderation.MuteUsersAsync([target], MuteMinutes);

        Announce(target, type, forVoters.Count, againstVoters.Count);
        NotifyResult(target, type, forVoters, againstVoters);
    }

    private void Announce(IUser target, VoteType type, int forCount, int againstCount)
    {
        if (!Settings.Chat.VoteAnnounceSanction) return;

        var template = type == VoteType.Ban
            ? Settings.Chat.VoteAnnounceBanText
            : Settings.Chat.VoteAnnounceMuteText;
        var text = Format(template, target.Name, forCount, againstCount);
        if (!string.IsNullOrWhiteSpace(text))
            Ext.Send(new ChatMsg(ChatType.Talk, text, Settings.Chat.VoteBubbleStyle));
    }

    private void OnAvatarsAdded(AvatarsEventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        List<(IUser User, VoteType Type, PendingSanction Pending)> toApply = [];

        lock (_lock)
        {
            // Drop expired deferred sanctions.
            foreach (var expired in _pendingSanctions.Where(kv => kv.Value.Expiry <= now).Select(kv => kv.Key).ToList())
                _pendingSanctions.Remove(expired);

            foreach (var avatar in e.Avatars)
            {
                if (avatar is not IUser user) continue;
                var nameLower = user.Name.ToLowerInvariant();

                foreach (var type in new[] { VoteType.Ban, VoteType.Mute })
                {
                    var key = (nameLower, type);
                    if (!_pendingSanctions.TryGetValue(key, out var pending) || pending.Expiry <= now) continue;

                    _pendingSanctions.Remove(key);

                    if (IsWhitelisted(user, user.Name)) continue;
                    if (IsVoteProtected(user)) continue;
                    var immunity = type == VoteType.Ban ? _immuneBanUntil : _immuneMuteUntil;
                    if (immunity.TryGetValue(user.Id, out var im) && im > now) continue;
                    if (!_moderation.CanModerate(ToModerationType(type), user)) continue;

                    toApply.Add((user, type, pending));
                }
            }
        }

        foreach (var (user, type, pending) in toApply)
            _ = ApplySanctionAsync(user, type, pending.ForVoters, pending.AgainstVoters);
    }

    private void NotifyResult(
        IUser target, VoteType type, IReadOnlyList<string> forVoters, IReadOnlyList<string> againstVoters)
    {
        _chatPage ??= Locator.Current.GetService<ChatPageViewModel>();
        var label = type == VoteType.Ban ? "vote-banned 1h" : "vote-muted 10min";
        var voters = FormatVoters("Pour", forVoters) + "\n" + FormatVoters("Contre", againstVoters);
        _chatPage?.AppendModerationNotification(
            target.Name, $"{label} ({forVoters.Count}/{againstVoters.Count})", voters);
        _chatPage?.AddVotedSanction(
            new VotedSanctionViewModel(target.Id, target.Name, type, forVoters, againstVoters));
    }

    private static string FormatVoters(string label, IReadOnlyList<string> voters) =>
        voters.Count > 0 ? $"{label} ({voters.Count}) : {string.Join(", ", voters)}" : $"{label} (0)";

    private static RoomModerationController.ModerationType ToModerationType(VoteType type) =>
        type == VoteType.Ban
            ? RoomModerationController.ModerationType.Ban
            : RoomModerationController.ModerationType.Mute;

    // Room owners and group admins are never a valid vote target, regardless of the
    // moderator's own rank (so the community can't brigade a trusted admin).
    private static bool IsVoteProtected(IUser user) => user.RightsLevel >= RightsLevel.GroupAdmin;

    private bool IsWhitelisted(IUser? target, string name)
    {
        if (_whitelistNames.Contains(name)) return true;
        if (target is not null && _whitelistIds.Contains(target.Id)) return true;
        return false;
    }

    private void OnLeftRoom()
    {
        lock (_lock)
        {
            _sessions.Clear();
            _messageCounts.Clear();
            _cooldownUntil.Clear();
            _immuneBanUntil.Clear();
            _immuneMuteUntil.Clear();
            _pendingSanctions.Clear();
            _lastRejectWhisper.Clear();
        }
    }

    [Intercept]
    private void OnBanSent(Intercept<BanUserMsg> e)
    {
        // Any ban (manual or vote-driven) pre-empts an in-progress ban vote on that user.
        var name = e.Msg.Name;
        if (string.IsNullOrEmpty(name) && e.Msg.Id is { } id &&
            _roomManager.Room is { } room && room.TryGetUserById(id, out var u))
            name = u.Name;
        if (!string.IsNullOrEmpty(name))
            CancelVote(name, VoteType.Ban);
    }

    [Intercept]
    private void OnUnbanSent(Intercept<UnbanUserMsg> e)
    {
        // Manual unban → 1h vote-ban immunity.
        lock (_lock)
            _immuneBanUntil[e.Msg.Id] = DateTimeOffset.UtcNow + ImmunityDuration;
    }

    [Intercept]
    private void OnMuteSent(Intercept<MuteUserMsg> e)
    {
        if (e.Msg.Minutes == 0)
        {
            // Manual unmute → 1h vote-mute immunity.
            lock (_lock)
                _immuneMuteUntil[e.Msg.Id] = DateTimeOffset.UtcNow + ImmunityDuration;
            return;
        }

        // Any mute (manual or vote-driven) pre-empts an in-progress mute vote on that user,
        // and the cooldown prevents a later vote from re-applying (and shortening) it.
        if (_roomManager.Room is { } room && room.TryGetUserById(e.Msg.Id, out var u))
            CancelVote(u.Name, VoteType.Mute);
    }

    /// <summary>Drops any in-progress or pending vote for the target and starts the cooldown.</summary>
    private void CancelVote(string name, VoteType type)
    {
        var key = (name.ToLowerInvariant(), type);
        lock (_lock)
        {
            _sessions.Remove(key);
            _pendingSanctions.Remove(key);
            _cooldownUntil[key] = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(Settings.Chat.VoteCooldownMinutes);
        }
    }

    /// <summary>
    /// Debug helper (used by the <c>/votetest</c> command): simulates a passed vote to
    /// exercise the full sanction + UI path solo, since a moderator can't cast real votes.
    /// </summary>
    public void TriggerTestSanction(IUser target, VoteType type)
    {
        int quorum = type == VoteType.Ban ? Settings.Chat.VoteBanQuorum : Settings.Chat.VoteMuteQuorum;
        var forVoters = Enumerable.Range(1, Math.Max(quorum, 1)).Select(i => $"tester{i}").ToList();
        _ = ApplySanctionAsync(target, type, forVoters, []);
    }

    public void AddToWhitelist(Id id, string name)
    {
        lock (_lock)
        {
            _whitelistIds.Add(id);
            _whitelistNames.Add(name);
            _whitelist.Ids.Add(id);
            if (!_whitelist.Names.Contains(name, StringComparer.OrdinalIgnoreCase))
                _whitelist.Names.Add(name);
            SaveWhitelist();
        }
    }

    public void AddToWhitelistByName(string name)
    {
        lock (_lock)
        {
            _whitelistNames.Add(name);
            if (!_whitelist.Names.Contains(name, StringComparer.OrdinalIgnoreCase))
                _whitelist.Names.Add(name);
            SaveWhitelist();
        }
    }

    private void LoadWhitelist()
    {
        var filePath = _appPathProvider.GetPath(AppPathKind.VoteWhitelist);
        if (!File.Exists(filePath)) return;

        try
        {
            _whitelist = JsonSerializer.Deserialize(
                File.ReadAllText(filePath),
                JsonSourceGenerationContext.Default.VoteWhitelistData
            ) ?? new();

            _whitelistIds.Clear();
            _whitelistNames.Clear();
            foreach (var id in _whitelist.Ids) _whitelistIds.Add(id);
            foreach (var name in _whitelist.Names) _whitelistNames.Add(name);
        }
        catch
        {
            _whitelist = new();
        }
    }

    private void SaveWhitelist()
    {
        try
        {
            File.WriteAllText(
                _appPathProvider.GetPath(AppPathKind.VoteWhitelist),
                JsonSerializer.Serialize(_whitelist, JsonSourceGenerationContext.Default.VoteWhitelistData)
            );
        }
        catch { }
    }
}
