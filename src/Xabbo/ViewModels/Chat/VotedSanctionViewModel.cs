using ReactiveUI;
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

    /// <summary>Set by a periodic sweep once <see cref="ExpiresAt"/> has passed.</summary>
    [Reactive] public bool IsExpired { get; set; }

    public bool IsBan => Type == VoteType.Ban;
    public string SanctionText => IsBan ? "banned 1h" : "muted 10min";
    public string UndoText => IsBan ? "Unban" : "Unmute";

    // Durations must match the sanctions applied by VoteModerationController
    // (BanDuration.Hour and MuteMinutes = 10).
    public DateTime ExpiresAt => Timestamp.AddMinutes(IsBan ? 60 : 10);
}
