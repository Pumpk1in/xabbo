using Xabbo.Messages.Flash;
using Xabbo.Extension;
using Xabbo.Core;
using Xabbo.Core.Game;
using Xabbo.Core.Messages.Outgoing;
using Xabbo.Core.Messages.Incoming;
using Xabbo.Core.Events;
using Xabbo.Services.Abstractions;
using Xabbo.Configuration;
using Xabbo.Controllers;
using Humanizer;

namespace Xabbo.Components;

[Intercept]
public partial class ClickToController(
    IExtension extension,
    IConfigProvider<AppConfig> config,
    XabbotComponent xabbot,
    RoomModerationController moderation,
    ProfileManager profileManager,
    FriendManager friendManager,
    RoomManager roomManager)
:
    ControllerBase(extension)
{
    private readonly XabbotComponent _xabbot = xabbot;
    private readonly IConfigProvider<AppConfig> _config = config;
    private readonly RoomModerationController _moderation = moderation;
    private readonly ProfileManager _profileManager = profileManager;
    private readonly FriendManager _friendManager = friendManager;
    private readonly RoomManager _roomManager = roomManager;

    private AppConfig Config => _config.Value;

    [Reactive] public bool Enabled { get; set; }

    [Reactive] public bool Mute { get; set; } = true;
    [Reactive] public int MuteValue { get; set; } = 1;
    [Reactive] public bool MuteInMinutes { get; set; }
    [Reactive] public bool MuteInHours { get; set; } = true;

    [Reactive] public bool Kick { get; set; }
    [Reactive] public bool Ban { get; set; }
    [Reactive] public bool BanHour { get; set; } = true;
    [Reactive] public bool BanDay { get; set; }
    [Reactive] public bool BanPerm { get; set; }

    [Reactive] public bool Bounce { get; set; }

    [Reactive] public bool Move { get; set; }

    // Variable names used by the game's debug object-variable function to set an avatar's position.
    // Confirmed by packet capture: -230 = position.x, -231 = position.y, type 1 = User.
    private const string VarPosX = "-230";
    private const string VarPosY = "-231";
    private const int VarTypeUser = 1;

    // Index of the avatar to move on the next tile click. Null = none.
    // Always cleared immediately after a move (never remembers the selected user).
    private int? _pendingMoveTarget;

    [Intercept(~ClientType.Shockwave)]
    [InterceptOut(nameof(Out.GetSelectedBadges))]
    void OnGetSelectedBadges(Intercept e)
    {
        if (!Enabled || !_roomManager.EnsureInRoom(out var room))
            return;

        HandleClickUser(room.GetAvatarById<IUser>(e.Packet.Read<Id>()));
    }

    private bool CanClickTo(IUser user) =>
        user.Id != _profileManager.UserData?.Id &&
        user.Name != _profileManager.UserData?.Name &&
        (!Config.General.ClickToIgnoresFriends || !_friendManager.IsFriend(user.Name));

    // Use LookTo on Shockwave as it doesn't have GetSelectedBadges
    // TODO: allow [Intercept(ClientType.Shockwave)] in Xabbo.Common.Generator
    // to filter a message to certain client types
    [Intercept]
    void HandleLookTo(Intercept e, LookToMsg lookTo)
    {
        if (!Enabled || Session.Is(ClientType.Modern)) return;

        if (!_roomManager.EnsureInRoom(out IRoom? room))
            return;

        e.Block();

        var usersOnTile = room.Avatars
            .OfType<IUser>()
            .Where(user =>
                user.Location == lookTo.Point &&
                CanClickTo(user)
            )
            .ToArray();

        if (usersOnTile is [ IUser user ])
        {
            HandleClickUser(user);
        }
        else if (usersOnTile.Length > 1)
        {
            _xabbot.ShowMessage($"Cannot click-to: more than one user on tile.");
        }
    }

    private void HandleClickUser(IUser? user)
    {
        if (!Enabled) return;

        if (!_roomManager.EnsureInRoom(out IRoom? room))
            return;

        // Ignore self.
        if (user is null ||
            user.Id == _profileManager.UserData?.Id ||
            user.Name == _profileManager.UserData?.Name)
        {
            return;
        }

        // Ignore friends if configured.
        if (Config.General.ClickToIgnoresFriends && _friendManager.IsFriend(user.Id))
            return;

        if (Mute)
        {
            if (!_moderation.CanMute)
                return;

            int muteMinutes = MuteValue;
            if (MuteInHours)
                muteMinutes *= 60;

            if (muteMinutes < 0)
                muteMinutes = 0;
            if (muteMinutes > 30000)
                muteMinutes = 30000;

            SendInfoMessage($"(click-muting user for {(MuteInMinutes ? "minute" : "hour").ToQuantity(MuteValue)}", user.Index);
            Send(new MuteUserMsg(user.Id, room.Id, muteMinutes));
        }
        else if (Kick)
        {
            if (!_roomManager.CanKick)
                return;

            SendInfoMessage("(click-kicking user)", user.Index);
            Send(new KickUserMsg(user.Id, user.Name));
        }
        else if (Ban)
        {
            if (!_roomManager.CanBan)
                return;

            BanDuration duration;
            string banText;

            if (BanDay)
            {
                duration = BanDuration.Day;
                banText = "for a day";
            }
            else if (BanHour)
            {
                duration = BanDuration.Hour;
                banText = "for an hour";
            }
            else if (BanPerm)
            {
                duration = BanDuration.Permanent;
                banText = "permanently";
            }
            else
                return;

            SendInfoMessage($"(click-banning user {banText})", user.Index);
            Send(new BanUserMsg(user.Id, user.Name, room.Id, duration));
        }
        else if (Bounce)
        {
            if (!_roomManager.IsOwner)
                return;

            Task.Run(async () => {
                SendInfoMessage($"(click-bouncing user)", user.Index);
                Send(new BanUserMsg(user.Id, user.Name, room.Id, BanDuration.Hour));
                if (Config.General.BounceUnbanDelay > 0)
                    await Task.Delay(Config.General.BounceUnbanDelay);
                Send(Out.UnbanUserFromRoom, user.Id, room.Id);
            });
        }
        else if (Move)
        {
            _pendingMoveTarget = user.Index;
            SendInfoMessage("(click-move: click a tile)", user.Index);
        }

    }

    [Intercept(~ClientType.Shockwave)]
    [InterceptOut(nameof(Out.MoveAvatar))]
    void OnWalk(Intercept e)
    {
        if (!Enabled || !Move || _pendingMoveTarget is not int index)
            return;

        var walk = e.Packet.Read<WalkMsg>();
        e.Block(); // Don't walk our own avatar.

        _pendingMoveTarget = null; // Safety: forget the selected user immediately.
        _ = MoveAvatarToAsync(index, walk.X, walk.Y);
    }

    // Each WiredSetObjectVariableValue sets a single variable and triggers one slide.
    // Sending X and Y back-to-back makes the server ignore Y (received mid-slide), so only
    // one axis moves. Send X, wait for the server to confirm the slide and finish animating,
    // then send Y.
    private async Task MoveAvatarToAsync(int index, int x, int y)
    {
        var tcs = new TaskCompletionSource<int>(); // resolves with AnimationTime
        void OnMove(AvatarWiredMovementEventArgs ev)
        {
            if (ev.Avatar.Index == index)
                tcs.TrySetResult(ev.Movement.AnimationTime);
        }

        _roomManager.AvatarWiredMovement += OnMove;
        try
        {
            Send(Out.WiredSetObjectVariableValue, VarTypeUser, index, VarPosX, x, 0);

            // Wait for the slide to start; time out if X already matches the current position.
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(1500));
            int animTime = completed == tcs.Task ? tcs.Task.Result : 0;

            if (animTime > 0)
                await Task.Delay(animTime + 100); // let the X slide finish before sending Y
        }
        finally
        {
            _roomManager.AvatarWiredMovement -= OnMove;
        }

        Send(Out.WiredSetObjectVariableValue, VarTypeUser, index, VarPosY, y, 0);
        SendInfoMessage($"(moved to {x},{y})", index);
    }

    private void SendInfoMessage(string message, int avatarIndex = -1) => Send(new AvatarWhisperMsg(message, avatarIndex));
}
