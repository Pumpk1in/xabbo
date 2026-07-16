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
/// Community vote-ban / vote-mute engine. Players type <c>/voteban</c>, <c>/votenoban</c>,
/// <c>/votemute</c> or <c>/votenomute</c> in room chat; when a net threshold + quorum is reached
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
        public HashSet<Id> WarnedDuplicate { get; } = [];
        public DateTimeOffset LastVoteTime { get; set; }

        public int For => Votes.Values.Count(v => v == VoteDirection.For);
        public int Against => Votes.Values.Count(v => v == VoteDirection.Against);
        public int Net => For - Against;
    }

    private readonly record struct PendingSanction(DateTimeOffset Expiry, int For, int Against);

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
        var verb = parts[0].ToLowerInvariant();

        if (!TryParseVerb(verb, out var type, out var direction))
        {
            // Only genuine (non-command) public chat counts toward voter eligibility.
            lock (_lock)
                _messageCounts[voter.Id] = _messageCounts.GetValueOrDefault(voter.Id) + 1;
            return;
        }

        if (parts.Length < 2)
        {
            // Bare command (no target) → whisper usage help.
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
        (IUser Target, VoteType Type, int For, int Against)? toApply = null;

        lock (_lock)
        {
            if (IsWhitelisted(target, targetName))
            {
                rejectWhisper = Format(Settings.Chat.VoteWhitelistedText, display, 0, 0);
            }
            else if (_messageCounts.GetValueOrDefault(voter.Id) < Settings.Chat.VoteMinMessages)
            {
                // Eligibility rule is intentionally never revealed — stay completely silent.
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
            else if (target is not null && !_moderation.CanModerate(ToModerationType(type), target))
            {
                rejectWhisper = Format(Settings.Chat.VoteNotAllowedText, display, 0, 0);
            }
            else
            {
                var key = (nameLower, type);
                if (!_sessions.TryGetValue(key, out var session) ||
                    (now - session.LastVoteTime).TotalMinutes > Settings.Chat.VoteSessionTtlMinutes)
                {
                    session = new VoteSession { Type = type };
                    _sessions[key] = session;
                }

                bool alreadyVoted = session.Votes.TryGetValue(voter.Id, out var existing);
                if (alreadyVoted && existing == direction)
                {
                    // Duplicate vote — warn once, then stay silent so we don't spam back.
                    if (session.WarnedDuplicate.Add(voter.Id))
                        rejectWhisper = Format(Settings.Chat.VoteAlreadyText, display, session.For, session.Against);
                }
                else
                {
                    session.Votes[voter.Id] = direction;
                    session.WarnedDuplicate.Remove(voter.Id);
                    session.LastVoteTime = now;

                    infoWhisper = Format(
                        alreadyVoted ? Settings.Chat.VoteChangedText : Settings.Chat.VoteCountedText,
                        display, session.For, session.Against);

                    if (session.Net >= Settings.Chat.VoteNetThreshold && session.For >= Settings.Chat.VoteQuorum)
                    {
                        var cooldown = now + TimeSpan.FromMinutes(Settings.Chat.VoteCooldownMinutes);
                        int f = session.For, a = session.Against;

                        if (target is not null && (type == VoteType.Ban ? _moderation.CanBan : _moderation.CanMute))
                        {
                            _sessions.Remove(key);
                            _cooldownUntil[key] = cooldown;
                            toApply = (target, type, f, a);
                        }
                        else if (target is null)
                        {
                            // Target isn't in the room (fled or stepped out) — apply on return, within the window.
                            _sessions.Remove(key);
                            _cooldownUntil[key] = cooldown;
                            _pendingSanctions[key] = new PendingSanction(now + DeferredSanctionWindow, f, a);
                        }
                    }
                }
            }
        }

        if (rejectWhisper is not null) WhisperReject(voter, rejectWhisper);
        else if (infoWhisper is not null) Whisper(voter, infoWhisper);

        if (toApply is { } app)
            _ = ApplySanctionAsync(app.Target, app.Type, app.For, app.Against);
    }

    private void Whisper(IUser voter, string message)
    {
        if (!Settings.Chat.VoteWhisperFeedback) return;
        Ext.Send(new WhisperMsg(voter.Name, message, Settings.Chat.BubbleStyle));
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

        Ext.Send(new WhisperMsg(voter.Name, message, Settings.Chat.BubbleStyle));
    }

    private async Task ApplySanctionAsync(IUser target, VoteType type, int forCount, int againstCount)
    {
        if (type == VoteType.Ban)
            await _moderation.BanUsersAsync([target], BanDuration.Hour);
        else
            await _moderation.MuteUsersAsync([target], MuteMinutes);

        NotifyResult(target, type, forCount, againstCount);
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
                    var immunity = type == VoteType.Ban ? _immuneBanUntil : _immuneMuteUntil;
                    if (immunity.TryGetValue(user.Id, out var im) && im > now) continue;
                    if (!_moderation.CanModerate(ToModerationType(type), user)) continue;

                    toApply.Add((user, type, pending));
                }
            }
        }

        foreach (var (user, type, pending) in toApply)
            _ = ApplySanctionAsync(user, type, pending.For, pending.Against);
    }

    private void NotifyResult(IUser target, VoteType type, int forCount, int againstCount)
    {
        _chatPage ??= Locator.Current.GetService<ChatPageViewModel>();
        var label = type == VoteType.Ban ? "vote-banned 1h" : "vote-muted 10min";
        _chatPage?.AppendModerationNotification(target.Name, $"{label} ({forCount}/{againstCount})");
        _chatPage?.AddVotedSanction(new VotedSanctionViewModel(target.Id, target.Name, type, forCount, againstCount));
    }

    private static RoomModerationController.ModerationType ToModerationType(VoteType type) =>
        type == VoteType.Ban
            ? RoomModerationController.ModerationType.Ban
            : RoomModerationController.ModerationType.Mute;

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
        int forCount = Math.Max(Settings.Chat.VoteNetThreshold, Settings.Chat.VoteQuorum);
        _ = ApplySanctionAsync(target, type, forCount, 0);
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
