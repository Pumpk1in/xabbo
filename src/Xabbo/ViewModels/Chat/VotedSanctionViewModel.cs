using Xabbo.Controllers;

namespace Xabbo.ViewModels;

public class VotedSanctionViewModel(long id, string name, VoteType type, int forCount, int againstCount)
    : ViewModelBase
{
    public long Id { get; } = id;
    public string Name { get; } = name;
    public VoteType Type { get; } = type;
    public int ForCount { get; } = forCount;
    public int AgainstCount { get; } = againstCount;
    public DateTime Timestamp { get; } = DateTime.Now;

    public bool IsBan => Type == VoteType.Ban;
    public string SanctionText => IsBan ? "banned 1h" : "muted 10min";
    public string UndoText => IsBan ? "Unban" : "Unmute";
}
