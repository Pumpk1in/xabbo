using Xabbo.Controllers;
using Xabbo.Core;
using Xabbo.Core.Game;

namespace Xabbo.Command.Modules;

/// <summary>
/// Debug command to test the vote-moderation feature solo. Since a moderator's own
/// <c>/voteban</c> chat is blocked outgoing (and never comes back as incoming chat that the
/// vote engine listens to), this command lets the moderator trigger a passed vote directly.
/// </summary>
[CommandModule]
public sealed class VoteTestCommand(RoomManager roomManager, VoteModerationController voteModeration) : CommandModule
{
    private readonly RoomManager _roomManager = roomManager;
    private readonly VoteModerationController _voteModeration = voteModeration;

    [Command("votetest")]
    private Task HandleVoteTestCommand(CommandArgs args)
    {
        if (args.Length < 1)
        {
            ShowMessage("Usage: /votetest <name> [ban|mute]");
            return Task.CompletedTask;
        }

        var name = args[0];
        var type = args.Length > 1 && args[1].Equals("mute", StringComparison.OrdinalIgnoreCase)
            ? VoteType.Mute : VoteType.Ban;

        if (_roomManager.Room is null || !_roomManager.Room.TryGetUserByName(name, out IUser? user))
        {
            ShowMessage($"User '{name}' not found in the room.");
            return Task.CompletedTask;
        }

        _voteModeration.TriggerTestSanction(user, type);
        ShowMessage($"Test vote-{type.ToString().ToLowerInvariant()} triggered on '{user.Name}'.");
        return Task.CompletedTask;
    }
}
