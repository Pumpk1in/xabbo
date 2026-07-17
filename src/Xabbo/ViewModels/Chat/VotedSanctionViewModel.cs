using ReactiveUI;
using Xabbo.Controllers;

namespace Xabbo.ViewModels;

public class VotedSanctionViewModel(
    long id, string name, VoteType type,
    IReadOnlyList<string> forVoters, IReadOnlyList<string> againstVoters)
    : ViewModelBase
{
    public long Id { get; } = id;
    public string Name { get; } = name;
    public VoteType Type { get; } = type;
    public IReadOnlyList<string> ForVoters { get; } = forVoters;
    public IReadOnlyList<string> AgainstVoters { get; } = againstVoters;
    public int ForCount => ForVoters.Count;
    public int AgainstCount => AgainstVoters.Count;

    /// <summary>Comma-joined voter names shown in the 👍 / 👎 tooltips.</summary>
    public string ForVotersText => ForVoters.Count > 0 ? string.Join(", ", ForVoters) : "—";
    public string AgainstVotersText => AgainstVoters.Count > 0 ? string.Join(", ", AgainstVoters) : "—";

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
