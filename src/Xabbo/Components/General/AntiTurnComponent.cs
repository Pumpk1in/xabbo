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
    private int _lastLookAtX, _lastLookAtY;
    private long _lastSelectedUser = -1;
    private System.DateTime _lastSelection = System.DateTime.MinValue;
    private System.DateTime _allowNextOwnTurnSetAt = System.DateTime.MinValue;

    private readonly IConfigProvider<AppConfig> _config;
    private readonly ProfileManager _profileManager;
    private readonly RoomManager _roomManager;

    private int _ownBodyDirection = -1;
    private int _ownHeadDirection = -1;
    private int _ownLocationX = int.MinValue;
    private int _ownLocationY = int.MinValue;
    private bool _ownDirectionInitialized;
    private bool _allowNextOwnTurn;
    private System.DateTime _forceBlockNextOwnTurnUntil = System.DateTime.MinValue;
    private System.DateTime _allowHandItemTurnUntil = System.DateTime.MinValue;

    public void ForceBlockNextOwnTurn() =>
        _forceBlockNextOwnTurnUntil = System.DateTime.Now.AddMilliseconds(500);

    public void AllowNextHandItemTurn() =>
        _allowHandItemTurnUntil = System.DateTime.Now.AddMilliseconds(500);

    public void InjectReturnLookToOwnDirection()
    {
        if (!_ownDirectionInitialized) return;
        _ = InjectReturnLookToAsync(_ownBodyDirection);
    }

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
        _forceBlockNextOwnTurnUntil = System.DateTime.MinValue;
        _allowHandItemTurnUntil = System.DateTime.MinValue;
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

        if (TurnOnReselect
            && (System.DateTime.Now - _lastSelection).TotalSeconds < ReselectThreshold
            && _lastLookAtX == look.X && _lastLookAtY == look.Y)
        {
            block = false;
            _allowNextOwnTurn = true;
            _allowNextOwnTurnSetAt = System.DateTime.Now;
        }

        _lastSelection = System.DateTime.Now;

        if (block) e.Block();

        _lastLookAtX = look.X;
        _lastLookAtY = look.Y;
    }

    [Intercept(~ClientType.Shockwave)]
    [InterceptOut(nameof(Out.WiredClickUser))]
    private void OnWiredClickUser(Intercept e)
    {
        if (!Enabled || !_ownDirectionInitialized) return;
        if (_allowNextOwnTurn) return; // re-select : laisser tourner naturellement

        _ = InjectReturnLookToAsync(_ownBodyDirection);
    }

    // Server ignores 45° rotations from a single LookTo. Send the opposite
    // direction first, then the target — same trick as TurnCommand.
    private async System.Threading.Tasks.Task InjectReturnLookToAsync(int dir)
    {
        var (ix, iy) = H.GetMagicVector((dir + 4) % 8);
        Ext.Send(new LookToMsg(ix, iy));

        await System.Threading.Tasks.Task.Delay(100);

        var (x, y) = H.GetMagicVector(dir);
        Ext.Send(new LookToMsg(x, y));
    }

    [Intercept(~ClientType.Shockwave)]
    [InterceptOut(nameof(Out.GetSelectedBadges))]
    private void OnRequestWearingBadges(Intercept e)
    {
        if (!Enabled) return;

        Id userId = e.Packet.Read<Id>();

        if (TurnOnReselect
            && (System.DateTime.Now - _lastSelection).TotalSeconds < ReselectThreshold
            && userId == _lastSelectedUser)
        {
            _allowNextOwnTurn = true;
            _allowNextOwnTurnSetAt = System.DateTime.Now;
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

            bool directionChanged = update.Direction != _ownBodyDirection || update.HeadDirection != _ownHeadDirection;

            if (!isMoving && System.DateTime.Now < _forceBlockNextOwnTurnUntil && directionChanged)
            {
                _forceBlockNextOwnTurnUntil = System.DateTime.MinValue;
                update.Direction = _ownBodyDirection;
                update.HeadDirection = _ownHeadDirection;
                modified = true;
            }
            else if (!isMoving && directionChanged && System.DateTime.Now < _allowHandItemTurnUntil)
            {
                _allowHandItemTurnUntil = System.DateTime.MinValue;
                _ownBodyDirection = update.Direction;
                _ownHeadDirection = update.HeadDirection;
            }
            else if (Enabled && !isMoving)
            {
                bool reselectActive = _allowNextOwnTurn
                    && (System.DateTime.Now - _allowNextOwnTurnSetAt).TotalSeconds < ReselectThreshold;

                if (directionChanged && reselectActive)
                {
                    _allowNextOwnTurn = false;
                    _ownBodyDirection = update.Direction;
                    _ownHeadDirection = update.HeadDirection;
                }
                else if (directionChanged)
                {
                    _allowNextOwnTurn = false;
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
