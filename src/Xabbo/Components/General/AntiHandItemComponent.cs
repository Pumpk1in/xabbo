using Xabbo.Messages.Flash;
using Xabbo.Extension;
using Xabbo.Core;
using Xabbo.Core.Game;

namespace Xabbo.Components;

[Intercept]
public partial class AntiHandItemComponent(
    IExtension extension,
    RoomManager roomManager,
    AntiTurnComponent antiTurnComponent
)
    : Component(extension)
{
    private readonly RoomManager _roomManager = roomManager;
    private readonly AntiTurnComponent _antiTurnComponent = antiTurnComponent;

    [Reactive] public bool DropHandItem { get; set; }
    [Reactive] public bool ReturnHandItem { get; set; }
    [Reactive] public bool ShouldMaintainDirection { get; set; }

    [InterceptIn(nameof(In.HandItemReceived))]
    private void HandleHandItemReceived(Intercept e)
    {
        if (ReturnHandItem)
        {
            int index = e.Packet.Read<int>();
            if (_roomManager.Room is not null &&
                _roomManager.Room.TryGetUserByIndex(index, out IUser? user))
            {
                e.Block();
                Ext.Send(Out.PassCarryItem, user.Id);
            }
        }
        else if (DropHandItem)
        {
            e.Block();
            Ext.Send(Out.DropCarryItem);
        }

        if (ShouldMaintainDirection)
        {
            _antiTurnComponent.ForceBlockNextOwnTurn();
            _antiTurnComponent.InjectReturnLookToOwnDirection();
        }
    }
}
