using Xabbo.Messages.Flash;
using Xabbo.Extension;
using Xabbo.Core;
using Xabbo.Core.Game;
using Xabbo.Core.Messages.Outgoing;
using Xabbo.Services.Abstractions;
using Xabbo.Configuration;

namespace Xabbo.Components;

[Intercept]
public partial class AntiTurnComponent : Component
{
    private long _lastSelectedUser = -1;
    private int _lastLookAtX, _lastLookAtY;
    private System.DateTime _lastSelection = System.DateTime.MinValue;

    private readonly IConfigProvider<AppConfig> _config;
    private readonly ProfileManager _profileManager;
    private readonly RoomManager _roomManager;

    private int _ownBodyDirection = -1;
    private int _ownHeadDirection = -1;
    private int _ownLocationX = int.MinValue;
    private int _ownLocationY = int.MinValue;
    private bool _ownDirectionInitialized;
    private bool _allowNextOwnTurn;

    private AppConfig Config => _config.Value;

    private bool Enabled => Config.Movement.NoTurn;
    private bool TurnOnReselect => Config.Movement.TurnOnReselectUser;
    private double ReselectThreshold => Config.Movement.ReselectThreshold;

    public AntiTurnComponent(
        IExtension extension,
        IConfigProvider<AppConfig> config,
        ProfileManager profileManager,
        RoomManager roomManager)
        : base(extension)
    {
        _config = config;
        _profileManager = profileManager;
        _roomManager = roomManager;

        _roomManager.Left += OnLeftRoom;
    }

    private void OnLeftRoom()
    {
        _ownDirectionInitialized = false;
        _ownBodyDirection = -1;
        _ownHeadDirection = -1;
        _ownLocationX = int.MinValue;
        _ownLocationY = int.MinValue;
        _allowNextOwnTurn = false;
    }

    private bool TryGetOwnIndex(out int index)
    {
        index = -1;
        if (_profileManager.UserData is not { } userData) return false;
        if (_roomManager.Room is not { } room) return false;
        if (!room.TryGetUserById(userData.Id, out IUser? self)) return false;

        index = self.Index;
        return true;
    }

    [Intercept]
    private void OnLookTo(Intercept e, LookToMsg look)
    {
        if (!Enabled) return;

        bool block = true;

        if (Client is ClientType.Shockwave)
        {
            if (Enabled && TurnOnReselect && (System.DateTime.Now - _lastSelection).TotalSeconds < ReselectThreshold)
            {
                if (_lastLookAtX == look.X && _lastLookAtY == look.Y)
                    block = false;
            }

            _lastSelection = System.DateTime.Now;
        }

        if (block) e.Block();

        _lastLookAtX = look.X;
        _lastLookAtY = look.Y;
    }

    [Intercept(~ClientType.Shockwave)]
    [InterceptOut(nameof(Out.GetSelectedBadges))]
    private void OnRequestWearingBadges(Intercept e)
    {
        Id userId = e.Packet.Read<Id>();

        if (Enabled && TurnOnReselect && (System.DateTime.Now - _lastSelection).TotalSeconds < ReselectThreshold)
        {
            if (userId == _lastSelectedUser)
                _allowNextOwnTurn = true;
        }

        _lastSelection = System.DateTime.Now;
        _lastSelectedUser = userId;
    }

    [InterceptIn(nameof(In.UserUpdate))]
    [Intercept(~ClientType.Shockwave)]
    private void OnUserUpdate(Intercept e)
    {
        if (!TryGetOwnIndex(out int ownIndex)) return;

        var updates = e.Packet.Read<AvatarStatus[]>();
        bool modified = false;

        foreach (var update in updates)
        {
            if (update.Index != ownIndex) continue;

            if (!_ownDirectionInitialized)
            {
                _ownBodyDirection = update.Direction;
                _ownHeadDirection = update.HeadDirection;
                _ownLocationX = update.Location.X;
                _ownLocationY = update.Location.Y;
                _ownDirectionInitialized = true;
                continue;
            }

            bool isMoving = update.MovingTo.HasValue
                || update.Location.X != _ownLocationX
                || update.Location.Y != _ownLocationY;

            if (Enabled && !isMoving)
            {
                bool directionChanged = update.Direction != _ownBodyDirection || update.HeadDirection != _ownHeadDirection;

                if (directionChanged && _allowNextOwnTurn)
                {
                    _allowNextOwnTurn = false;
                    _ownBodyDirection = update.Direction;
                    _ownHeadDirection = update.HeadDirection;
                }
                else if (directionChanged)
                {
                    update.Direction = _ownBodyDirection;
                    update.HeadDirection = _ownHeadDirection;
                    modified = true;
                }
            }
            else
            {
                _ownBodyDirection = update.Direction;
                _ownHeadDirection = update.HeadDirection;
                _ownLocationX = update.Location.X;
                _ownLocationY = update.Location.Y;
            }
        }

        if (modified)
        {
            e.Packet.Clear();
            e.Packet.Write(updates);
        }
    }
}
