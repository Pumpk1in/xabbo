using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Splat;
using Xabbo.Messages.Flash;
using Xabbo.Core;
using Xabbo.Core.Game;
using Xabbo.Core.Events;
using Xabbo.Core.Messages.Outgoing;
using Xabbo.Models;
using Xabbo.Models.Enums;
using Xabbo.Serialization;
using Xabbo.Services.Abstractions;
using Xabbo.ViewModels;

namespace Xabbo.Command.Modules;

[CommandModule]
public sealed class ModerationCommands(RoomManager roomManager, ProfileManager profileManager, IAppPathProvider appPathProvider) : CommandModule
{
    private readonly RoomManager _roomManager = roomManager;
    private readonly ProfileManager _profileManager = profileManager;
    private readonly IAppPathProvider _appPathProvider = appPathProvider;

    private readonly ConcurrentDictionary<string, int> _muteList = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BanDuration> _banList = new(StringComparer.OrdinalIgnoreCase);
    // Auto-ban glob rules compiled for the current room (recompiled on room entry / edit).
    private readonly List<(Regex Regex, string Pattern)> _autoBanRules = new();
    private BanDuration _autoBanDuration = BanDuration.Permanent;
    private DeferredModerationData _deferredData = new();
    private readonly object _deferredLock = new();
    private ChatPageViewModel? _chatPage;
    private CancellationTokenSource? _roomEntryCts;

    private void NotifyChatLog(string userName, string action)
    {
        _chatPage ??= Locator.Current.GetService<ChatPageViewModel>();
        _chatPage?.AppendModerationNotification(userName, action);
    }

    protected override void OnInitialize()
    {
        _roomManager.Left += RoomManager_Left;
        _roomManager.Entered += RoomManager_Entered;
        _roomManager.AvatarsAdded += OnAvatarsAdded;

        LoadDeferredData();

        IsAvailable = true;
    }

    private void LoadDeferredData()
    {
        var filePath = _appPathProvider.GetPath(AppPathKind.DeferredBans);
        if (File.Exists(filePath))
        {
            try
            {
                _deferredData = JsonSerializer.Deserialize(
                    File.ReadAllText(filePath),
                    JsonSourceGenerationContext.Default.DeferredModerationData
                ) ?? new();

                // Rebuild inner dictionaries with OrdinalIgnoreCase — lost during JSON deserialization.
                _deferredData.Bans = _deferredData.Bans.ToDictionary(
                    kv => kv.Key,
                    kv => new Dictionary<string, int>(kv.Value, StringComparer.OrdinalIgnoreCase));
                _deferredData.Mutes = _deferredData.Mutes.ToDictionary(
                    kv => kv.Key,
                    kv => new Dictionary<string, int>(kv.Value, StringComparer.OrdinalIgnoreCase));
            }
            catch
            {
                _deferredData = new();
            }
        }
    }

    private void SaveDeferredData()
    {
        try
        {
            File.WriteAllText(
                _appPathProvider.GetPath(AppPathKind.DeferredBans),
                JsonSerializer.Serialize(_deferredData, JsonSourceGenerationContext.Default.DeferredModerationData)
            );
        }
        catch { }
    }

    private void MuteUser(IUser user, int minutes)
    {
        if (_roomManager.EnsureInRoom(out var room))
            Ext.Send(new MuteUserMsg(user, room.Id, minutes));
    }

    private void UnmuteUser(IUser user)
    {
        if (_roomManager.EnsureInRoom(out var room))
            Ext.Send(new MuteUserMsg(user, room.Id, 0));
    }

    private void KickUser(IUser user) => Ext.Send(new KickUserMsg(user));

    private void BanUser(IUser user, BanDuration duration)
    {
        if (_roomManager.EnsureInRoom(out var room))
            Ext.Send(new BanUserMsg(user, room.Id, duration));
    }

    private void UnbanUser(Id userId)
    {
        if (_roomManager.EnsureInRoom(out var room))
            Ext.Send(Out.UnbanUserFromRoom, userId, room.Id);
    }

    public void AddToBanList(string userName, BanDuration duration)
    {
        _banList[userName] = duration;

        // Persist
        if (_roomManager.Room is { Id: var roomId })
        {
            if (!_deferredData.Bans.TryGetValue(roomId, out var roomBans))
                _deferredData.Bans[roomId] = roomBans = new(StringComparer.OrdinalIgnoreCase);
            roomBans[userName] = (int)duration;
            SaveDeferredData();
        }

        NotifyPendingBan(userName, duration);
    }

    private void NotifyPendingBan(string userName, BanDuration duration)
    {
        _chatPage ??= Locator.Current.GetService<ChatPageViewModel>();
        _chatPage?.AddPendingBan(userName, FormatBanDuration(duration), () => CancelDeferredBan(userName));
    }

    private void CancelDeferredBan(string userName)
    {
        _banList.TryRemove(userName, out _);
        if (_roomManager.Room is { Id: var roomId })
            lock (_deferredLock)
            {
                if (_deferredData.Bans.TryGetValue(roomId, out var roomBans))
                {
                    roomBans.Remove(userName);
                    if (roomBans.Count == 0)
                        _deferredData.Bans.Remove(roomId);
                    SaveDeferredData();
                }
            }
        NotifyChatLog(userName, "deferred ban cancelled");
    }

    public void CleanupDeferredBans(IEnumerable<string> bannedNames, long roomId)
    {
        _deferredData.Bans.TryGetValue(roomId, out var roomBans);

        var cleaned = false;
        foreach (var name in bannedNames)
        {
            var inBanList = _banList.TryRemove(name, out _);
            var inDeferredData = roomBans?.Remove(name) ?? false;

            if (inBanList || inDeferredData)
            {
                NotifyChatLog(name, "already banned, removed from deferred list");
                _chatPage?.RemovePendingBan(name);
                cleaned = true;
            }
        }
        if (cleaned)
            SaveDeferredData();
    }

    private void RoomManager_Entered(RoomEventArgs e)
    {
        _banList.Clear();
        _muteList.Clear();

        var roomId = (long)e.Room.Id;

        // Load deferred bans for this room
        if (_deferredData.Bans.TryGetValue(roomId, out var roomBans))
        {
            foreach (var (userName, duration) in roomBans)
                _banList[userName] = (BanDuration)duration;
        }

        // Load deferred mutes for this room
        if (_deferredData.Mutes.TryGetValue(roomId, out var roomMutes))
        {
            foreach (var (userName, minutes) in roomMutes)
                _muteList[userName] = minutes;
        }

        // Load & compile auto-ban patterns for this room.
        CompileAutoBanRules(roomId);

        // Reflect the loaded deferred bans in the moderation panel's pending list.
        _chatPage ??= Locator.Current.GetService<ChatPageViewModel>();
        _chatPage?.ClearPendingBans();
        foreach (var (userName, duration) in _banList)
            NotifyPendingBan(userName, duration);

        // Reflect this room's auto-ban rules in the panel's admin section.
        SyncAutoBanRulesToPanel(roomId);

        // Apply immediately to targets already present in the room (returned while we were away).
        foreach (var name in _banList.Keys.Concat(_muteList.Keys).ToList())
            if (e.Room.TryGetUserByName(name, out IUser? user))
                _ = ApplyDeferredSanctionAsync(user);

        // Auto-ban present users matching a pattern (skip those an exact deferred sanction already covers).
        if (_autoBanRules.Count > 0)
            foreach (var user in e.Room.Users.ToList())
                if (!_banList.ContainsKey(user.Name) && !_muteList.ContainsKey(user.Name))
                    _ = ApplyAutoBanIfMatchAsync(user);

        // Schedule a silent ban list check to clean up deferred bans already applied
        if (_deferredData.Bans.ContainsKey(roomId))
        {
            var cts = new CancellationTokenSource();
            Interlocked.Exchange(ref _roomEntryCts, cts)?.Cancel();
            _ = CheckDeferredBansAsync(roomId, cts.Token);
        }
        else
        {
            Interlocked.Exchange(ref _roomEntryCts, null)?.Cancel();
        }
    }

    private async Task CheckDeferredBansAsync(long roomId, CancellationToken ct)
    {
        try
        {
            await Task.Delay(5000, ct);

            if (ct.IsCancellationRequested) return;
            if (!_roomManager.CanBan) return;
            if (_roomManager.Room is not { Id: var currentRoomId } || (long)currentRoomId != roomId) return;

            var users = await Ext.RequestAsync(new GetBannedUsersMsg(roomId), timeout: 10000, cancellationToken: ct);
            CleanupDeferredBans(users.Select(u => u.Name), roomId);
        }
        catch { }
    }

    private void RoomManager_Left()
    {
        _muteList.Clear();
        _banList.Clear();
        _autoBanRules.Clear();
        _chatPage?.ClearPendingBans();
        _chatPage?.ClearAutoBanRules();
        Interlocked.Exchange(ref _roomEntryCts, null)?.Cancel();
    }

    private void OnAvatarsAdded(AvatarsEventArgs e)
    {
        foreach (var avatar in e.Avatars)
            if (avatar is User user)
                _ = HandleUserEntryAsync(user);
    }

    private async Task HandleUserEntryAsync(IUser user)
    {
        // Exact deferred ban/mute takes priority; only fall back to pattern auto-ban if nothing exact matched.
        if (!await ApplyDeferredSanctionAsync(user))
            await ApplyAutoBanIfMatchAsync(user);
    }

    private async Task<bool> ApplyDeferredSanctionAsync(IUser user)
    {
        if (_banList.TryGetValue(user.Name, out BanDuration banDuration))
        {
            await BanUserNow(user, banDuration, SanctionKind.Deferred, "deferred",
                $"banned {FormatBanDuration(banDuration)} (deferred)");
            _banList.TryRemove(user.Name, out _);

            // Remove from persistent storage
            if (_roomManager.Room is { Id: var roomId })
                lock (_deferredLock)
                {
                    if (_deferredData.Bans.TryGetValue(roomId, out var roomBans))
                    {
                        roomBans.Remove(user.Name);
                        if (roomBans.Count == 0)
                            _deferredData.Bans.Remove(roomId);
                        SaveDeferredData();
                    }
                }
            return true;
        }
        else if (_muteList.TryGetValue(user.Name, out int muteDuration))
        {
            ShowMessage($"Muting user '{user.Name}'");
            NotifyChatLog(user.Name, "muted (deferred)");
            await Task.Delay(100);
            MuteUser(user, muteDuration);
            _muteList.TryRemove(user.Name, out _);

            // Remove from persistent storage
            if (_roomManager.Room is { Id: var roomId })
                lock (_deferredLock)
                {
                    if (_deferredData.Mutes.TryGetValue(roomId, out var roomMutes))
                    {
                        roomMutes.Remove(user.Name);
                        if (roomMutes.Count == 0)
                            _deferredData.Mutes.Remove(roomId);
                        SaveDeferredData();
                    }
                }
            return true;
        }
        return false;
    }

    /// <summary>Kicks from the room group if needed, then bans; shared by deferred and auto-ban paths.</summary>
    private async Task BanUserNow(IUser user, BanDuration duration, SanctionKind kind, string detail, string chatLogAction)
    {
        if (_roomManager.Room?.Data is { IsGroupRoom: true } data && Session.Is(ClientType.Modern))
        {
            Ext.Send(new KickGroupMemberMsg(data.GroupId, user.Id));
            ShowMessage($"Kicking user '{user.Name}' from room group");
            NotifyChatLog(user.Name, "kicked from room group");
            await Task.Delay(1500);
        }
        ShowMessage($"Banning user '{user.Name}' {FormatBanDuration(duration)}");
        NotifyChatLog(user.Name, chatLogAction);
        await Task.Delay(100);
        BanUser(user, duration);

        _chatPage ??= Locator.Current.GetService<ChatPageViewModel>();
        _chatPage?.AddAppliedModerationBan(user.Id, user.Name, kind, detail, duration);
        _chatPage?.RemovePendingBan(user.Name);
    }

    private async Task ApplyAutoBanIfMatchAsync(IUser user)
    {
        if (_autoBanRules.Count == 0 || !CanAutoBan(user))
            return;

        foreach (var (regex, pattern) in _autoBanRules)
        {
            if (regex.IsMatch(user.Name))
            {
                await BanUserNow(user, _autoBanDuration, SanctionKind.Auto, $"`{pattern}`",
                    $"auto-banned {FormatBanDuration(_autoBanDuration)} (pattern `{pattern}`)");
                return;
            }
        }
    }

    // Never auto-ban yourself, staff, or anyone with equal/greater rights; only when we can ban at all.
    private bool CanAutoBan(IUser user) =>
        _roomManager.CanBan &&
        !string.Equals(user.Name, _profileManager.UserData?.Name, StringComparison.OrdinalIgnoreCase) &&
        !user.IsStaff &&
        _roomManager.RightsLevel > user.RightsLevel;

    private void CompileAutoBanRules(long roomId)
    {
        _autoBanRules.Clear();
        if (_deferredData.AutoBanPatterns.TryGetValue(roomId, out var patterns))
            foreach (var pattern in patterns)
                _autoBanRules.Add((GlobToRegex(pattern), pattern));

        _autoBanDuration = _deferredData.AutoBanDuration.TryGetValue(roomId, out var d)
            ? (BanDuration)d
            : BanDuration.Permanent;
    }

    // Standard shell glob: * = zero or more chars, ? = one char, case-insensitive, whole-name match.
    private static Regex GlobToRegex(string glob) => new(
        "^" + Regex.Escape(glob).Replace("\\*", ".*").Replace("\\?", ".") + "$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private void AddAutoBanPattern(long roomId, string pattern)
    {
        if (!_deferredData.AutoBanPatterns.TryGetValue(roomId, out var list))
            _deferredData.AutoBanPatterns[roomId] = list = new();
        if (!list.Contains(pattern, StringComparer.OrdinalIgnoreCase))
            list.Add(pattern);
        SaveDeferredData();
        CompileAutoBanRules(roomId);
    }

    private bool RemoveAutoBanPattern(long roomId, string pattern)
    {
        if (!_deferredData.AutoBanPatterns.TryGetValue(roomId, out var list))
            return false;
        int removed = list.RemoveAll(p => string.Equals(p, pattern, StringComparison.OrdinalIgnoreCase));
        if (list.Count == 0)
            _deferredData.AutoBanPatterns.Remove(roomId);
        if (removed == 0)
            return false;
        SaveDeferredData();
        CompileAutoBanRules(roomId);
        return true;
    }

    private void ClearAutoBanPatterns(long roomId)
    {
        _deferredData.AutoBanPatterns.Remove(roomId);
        SaveDeferredData();
        CompileAutoBanRules(roomId);
        SyncAutoBanRulesToPanel(roomId);
    }

    private void SetAutoBanDuration(long roomId, BanDuration duration)
    {
        _deferredData.AutoBanDuration[roomId] = (int)duration;
        SaveDeferredData();
        _autoBanDuration = duration;
        SyncAutoBanRulesToPanel(roomId);
    }

    /// <summary>Adds a pattern and applies it live; shared by the /autoban command and the panel's Add button.</summary>
    private void ApplyNewAutoBanPattern(long roomId, string pattern)
    {
        AddAutoBanPattern(roomId, pattern);
        SyncAutoBanRulesToPanel(roomId);
        ShowMessage($"Added auto-ban pattern '{pattern}' ({FormatBanDuration(_autoBanDuration)}).");
        NotifyChatLog(pattern, "added as auto-ban pattern");
        // Apply the new rule to users already in the room.
        if (_roomManager.Room is { } room)
            foreach (var user in room.Users.ToList())
                _ = ApplyAutoBanIfMatchAsync(user);
    }

    /// <summary>Pushes the current room's auto-ban rules to the moderation panel's admin section.</summary>
    private void SyncAutoBanRulesToPanel(long roomId)
    {
        _chatPage ??= Locator.Current.GetService<ChatPageViewModel>();
        var patterns = _deferredData.AutoBanPatterns.TryGetValue(roomId, out var list)
            ? list.ToList()
            : new List<string>();
        _chatPage?.SetAutoBanRules(
            patterns,
            FormatBanDuration(_autoBanDuration),
            pattern => ApplyNewAutoBanPattern(roomId, pattern),
            pattern =>
            {
                if (RemoveAutoBanPattern(roomId, pattern))
                {
                    ShowMessage($"Removed auto-ban pattern '{pattern}'.");
                    NotifyChatLog(pattern, "removed auto-ban pattern");
                    SyncAutoBanRulesToPanel(roomId);
                }
            });
    }

    [Command("mute", SupportedClients = ClientType.Modern)]
    public Task HandleMuteCommand(CommandArgs args)
    {
        if (args.Length < 2)
        {
            ShowMessage("/mute <name> <duration>[m|h]");
        }
        else if (!_roomManager.IsInRoom)
        {
            ShowMessage("Reload the room to initialize room state.");
        }
        else if (!_roomManager.CanMute)
        {
            ShowMessage("You do not have permission to mute in this room.");
        }
        else
        {
            string userName = args[0];

            if (_roomManager.Room is not null &&
                _roomManager.Room.TryGetUserByName(userName, out IUser? user))
            {
                bool isHours = false;
                string durationString = args[1];

                if (durationString.EndsWith("m") || durationString.EndsWith("h"))
                {
                    if (durationString.EndsWith("h"))
                        isHours = true;

                    durationString = durationString.Substring(0, durationString.Length - 1);
                }

                if (!int.TryParse(durationString, out int inputDuration) || inputDuration <= 0)
                {
                    ShowMessage($"Invalid argument for duration: {args[1]}");
                }
                else
                {
                    int duration = inputDuration;
                    if (isHours) duration *= 60;

                    if (duration > 30000)
                    {
                        ShowMessage($"Maximum mute time is 500 hours or 30,000 minutes.");
                    }
                    else
                    {
                        ShowMessage($"Muting user '{user.Name}' for {inputDuration} {(isHours ? "hour(s)" : "minute(s)")}");
                        NotifyChatLog(user.Name, $"muted for {inputDuration} {(isHours ? "hour(s)" : "minute(s)")}");
                        MuteUser(user, duration);
                    }
                }
            }
            else
            {
                ShowMessage($"Unable to find user '{userName}' to mute.");
            }
        }
        return Task.CompletedTask;
    }

    [Command("unmute", SupportedClients = ClientType.Modern)]
    public Task HandleUnmuteCommand(CommandArgs args)
    {
        if (args.Length < 1) return Task.CompletedTask;

        if (!_roomManager.IsInRoom)
        {
            ShowMessage("Reload the room to initialize room state.");
            return Task.CompletedTask;
        }

        if (!_roomManager.CanMute)
        {
            ShowMessage("You do not have permission to unmute in this room.");
            return Task.CompletedTask;
        }

        string userName = args[0];

        if (_roomManager.Room is not null &&
            _roomManager.Room.TryGetAvatarByName(userName, out IUser? user))
        {
            ShowMessage($"Unmuting user '{user.Name}'");
            NotifyChatLog(user.Name, "unmuted");
            UnmuteUser(user);
        }
        else
        {
            ShowMessage($"Unable to find user '{userName}' to unmute.");
        }
        return Task.CompletedTask;
    }

    [Command("kick")]
    public Task HandleKickCommand(CommandArgs args)
    {
        if (args.Length < 1) return Task.CompletedTask;

        if (!_roomManager.IsInRoom)
        {
            ShowMessage("Reload the room to initialize room state.");
            return Task.CompletedTask;
        }

        if (!_roomManager.CanKick)
        {
            ShowMessage("You do not have permission to kick in this room.");
            return Task.CompletedTask;
        }

        string userName = args[0];

        if (_roomManager.Room is not null &&
            _roomManager.Room.TryGetUserByName(userName, out IUser? user))
        {
            ShowMessage($"Kicking user '{user.Name}'");
            NotifyChatLog(user.Name, "kicked");
            KickUser(user);
        }
        else
        {
            ShowMessage($"Unable to find user '{userName}' to kick.");
        }
        return Task.CompletedTask;
    }

    [Command("ban")]
    public async Task HandleBanCommand(CommandArgs args)
    {
        if (args.Length < 1) return;

        var banDuration = BanDuration.Hour;

        if (args.Length > 1)
        {
            switch (args[1].ToLower())
            {
                case "hour":
                    banDuration = BanDuration.Hour;
                    break;
                case "day":
                    banDuration = BanDuration.Day;
                    break;
                case "perm":
                    banDuration = BanDuration.Permanent;
                    break;
                default:
                    ShowMessage($"Unknown ban type '{args[1]}'.");
                    return;
            }
        }

        string durationString = FormatBanDuration(banDuration);
        string userName = args[0];

        if (_roomManager.Room is not null &&
            _roomManager.Room.TryGetUserByName(userName, out IUser? user))
        {
            if (_roomManager.Room?.Data is { IsGroupRoom: true } data && Session.Is(ClientType.Modern))
            {
                Ext.Send(new KickGroupMemberMsg(data.GroupId, user.Id));
                ShowMessage($"Kicking user '{user.Name}' from room group");
                NotifyChatLog(user.Name, "kicked from room group");
                await Task.Delay(1500);
            }
            ShowMessage($"Banning user '{user.Name}' {durationString}");
            NotifyChatLog(user.Name, $"banned {durationString}");
            BanUser(user, banDuration);
        }
        else
        {
            ShowMessage($"User '{userName}' not found, will be banned {durationString} upon next entry to this room.");
            NotifyChatLog(userName, $"will be banned {durationString} upon next entry");
            AddToBanList(userName, banDuration);
        }
    }

    [Command("autoban", SupportedClients = ClientType.Modern)]
    public Task HandleAutoBanCommand(CommandArgs args)
    {
        if (!_roomManager.IsInRoom || _roomManager.Room is not { Id: var id })
        {
            ShowMessage("Reload the room to initialize room state.");
            return Task.CompletedTask;
        }
        long roomId = (long)id;

        // /autoban  |  /autoban list  -> show current patterns.
        if (args.Length == 0 || args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            if (!_deferredData.AutoBanPatterns.TryGetValue(roomId, out var list) || list.Count == 0)
                ShowMessage("No auto-ban patterns for this room. Add one with /autoban <pattern> (e.g. *khur*).");
            else
                ShowMessage($"Auto-ban patterns ({FormatBanDuration(_autoBanDuration)}): {string.Join(" | ", list)}");
            return Task.CompletedTask;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "del" or "remove":
                if (args.Length < 2)
                    ShowMessage("/autoban del <pattern>");
                else if (RemoveAutoBanPattern(roomId, args[1]))
                    ShowMessage($"Removed auto-ban pattern '{args[1]}'.");
                else
                    ShowMessage($"Auto-ban pattern '{args[1]}' not found.");
                break;

            case "clear":
                ClearAutoBanPatterns(roomId);
                ShowMessage("Cleared all auto-ban patterns for this room.");
                break;

            case "duration":
                BanDuration? d = args.Length < 2 ? null : args[1].ToLowerInvariant() switch
                {
                    "hour" => BanDuration.Hour,
                    "day" => BanDuration.Day,
                    "perm" => BanDuration.Permanent,
                    _ => null
                };
                if (d is null)
                    ShowMessage("/autoban duration <hour|day|perm>");
                else
                {
                    SetAutoBanDuration(roomId, d.Value);
                    ShowMessage($"Auto-ban duration set to {FormatBanDuration(d.Value)}.");
                }
                break;

            default:
                // Anything else is a glob pattern to add.
                ApplyNewAutoBanPattern(roomId, args[0]);
                break;
        }

        return Task.CompletedTask;
    }

    private static string FormatBanDuration(BanDuration duration) => duration switch
    {
        BanDuration.Hour => "for an hour",
        BanDuration.Day => "for a day",
        BanDuration.Permanent => "permanently",
        _ => "for an hour"
    };
}
