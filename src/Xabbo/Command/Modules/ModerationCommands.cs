using System.Collections.Concurrent;
using System.Text.Json;
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
public sealed class ModerationCommands(RoomManager roomManager, IAppPathProvider appPathProvider) : CommandModule
{
    private readonly RoomManager _roomManager = roomManager;
    private readonly IAppPathProvider _appPathProvider = appPathProvider;

    private readonly ConcurrentDictionary<string, int> _muteList = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BanDuration> _banList = new(StringComparer.OrdinalIgnoreCase);
    private DeferredModerationData _deferredData = new();
    private ChatPageViewModel? _chatPage;

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
    }

    public void CleanupDeferredBans(IEnumerable<string> bannedNames, long roomId)
    {
        var cleaned = false;
        foreach (var name in bannedNames)
        {
            if (_banList.TryRemove(name, out _))
            {
                if (_deferredData.Bans.TryGetValue(roomId, out var roomBans))
                    roomBans.Remove(name);
                NotifyChatLog(name, "already banned, removed from deferred list");
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
    }

    private void RoomManager_Left()
    {
        _muteList.Clear();
        _banList.Clear();
    }

    private void OnAvatarsAdded(AvatarsEventArgs e)
    {
        if (e.Avatars is not [User user]) return;

        Task.Run(async () =>
        {
            if (_banList.TryGetValue(user.Name, out BanDuration banDuration))
            {
                if (_roomManager.Room?.Data is { IsGroupRoom: true } data && Session.Is(ClientType.Modern))
                {
                    Ext.Send(new KickGroupMemberMsg(data.GroupId, user.Id));
                    ShowMessage($"Kicking user '{user.Name}' from room group");
                    NotifyChatLog(user.Name, "kicked from room group");
                    await Task.Delay(1000);
                }
                ShowMessage($"Banning user '{user.Name}'");
                NotifyChatLog(user.Name, "banned (deferred)");
                await Task.Delay(100);
                BanUser(user, banDuration);
                _banList.TryRemove(user.Name, out _);

                // Remove from persistent storage
                if (_roomManager.Room is { Id: var roomId } &&
                    _deferredData.Bans.TryGetValue(roomId, out var roomBans))
                {
                    roomBans.Remove(user.Name);
                    if (roomBans.Count == 0)
                        _deferredData.Bans.Remove(roomId);
                    SaveDeferredData();
                }
            }
            else if (_muteList.TryGetValue(user.Name, out int muteDuration))
            {
                ShowMessage($"Muting user '{user.Name}'");
                NotifyChatLog(user.Name, "muted (deferred)");
                await Task.Delay(100);
                MuteUser(user, muteDuration);
                _muteList.TryRemove(user.Name, out _);

                // Remove from persistent storage
                if (_roomManager.Room is { Id: var roomId } &&
                    _deferredData.Mutes.TryGetValue(roomId, out var roomMutes))
                {
                    roomMutes.Remove(user.Name);
                    if (roomMutes.Count == 0)
                        _deferredData.Mutes.Remove(roomId);
                    SaveDeferredData();
                }
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
        string durationString = "for an hour";

        if (args.Length > 1)
        {
            switch (args[1].ToLower())
            {
                case "hour":
                    banDuration = BanDuration.Hour;
                    durationString = "for an hour";
                    break;
                case "day":
                    banDuration = BanDuration.Day;
                    durationString = "for a day";
                    break;
                case "perm":
                    banDuration = BanDuration.Permanent;
                    durationString = "permanently";
                    break;
                default:
                    ShowMessage($"Unknown ban type '{args[1]}'.");
                    return;
            }
        }

        string userName = args[0];

        if (_roomManager.Room is not null &&
            _roomManager.Room.TryGetUserByName(userName, out IUser? user))
        {
            if (_roomManager.Room?.Data is { IsGroupRoom: true } data && Session.Is(ClientType.Modern))
            {
                Ext.Send(new KickGroupMemberMsg(data.GroupId, user.Id));
                ShowMessage($"Kicking user '{user.Name}' from room group");
                NotifyChatLog(user.Name, "kicked from room group");
                await Task.Delay(1000);
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
}
