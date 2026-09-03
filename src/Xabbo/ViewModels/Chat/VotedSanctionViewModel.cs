using Avalonia.Media;
using ReactiveUI;
using Xabbo.Controllers;
using Xabbo.Core;

namespace Xabbo.ViewModels;

/// <summary>Distinguishes how a sanction was applied, for display in the moderation panel.</summary>
public enum SanctionKind { Vote, Deferred, Auto }

public class VotedSanctionViewModel : ViewModelBase
{
    public long Id { get; }
    public string Name { get; }
    public VoteType Type { get; }
    public SanctionKind Kind { get; }
    public IReadOnlyList<string> ForVoters { get; }
    public IReadOnlyList<string> AgainstVoters { get; }

    // Ban duration for Deferred/Auto bans; null for votes (fixed 1h ban / 10min mute).
    private readonly BanDuration? _banDuration;
    // Extra context shown in place of the vote counts for non-vote sanctions (the matched pattern, "deferred", …).
    private readonly string _detail;

    /// <summary>Vote-applied sanction (with voter lists).</summary>
    public VotedSanctionViewModel(
        long id, string name, VoteType type,
        IReadOnlyList<string> forVoters, IReadOnlyList<string> againstVoters)
    {
        Id = id;
        Name = name;
        Type = type;
        Kind = SanctionKind.Vote;
        ForVoters = forVoters;
        AgainstVoters = againstVoters;
        _banDuration = null;
        _detail = "";
    }

    /// <summary>Deferred or auto (pattern) ban applied by the moderation module (no voters).</summary>
    public VotedSanctionViewModel(long id, string name, SanctionKind kind, string detail, BanDuration duration)
    {
        Id = id;
        Name = name;
        Type = VoteType.Ban;
        Kind = kind;
        ForVoters = [];
        AgainstVoters = [];
        _banDuration = duration;
        _detail = detail;
    }

    public int ForCount => ForVoters.Count;
    public int AgainstCount => AgainstVoters.Count;

    /// <summary>Comma-joined voter names shown in the 👍 / 👎 tooltips.</summary>
    public string ForVotersText => ForVoters.Count > 0 ? string.Join(", ", ForVoters) : "—";
    public string AgainstVotersText => AgainstVoters.Count > 0 ? string.Join(", ", AgainstVoters) : "—";

    public DateTime Timestamp { get; } = DateTime.Now;

    /// <summary>Set by a periodic sweep once <see cref="ExpiresAt"/> has passed.</summary>
    [Reactive] public bool IsExpired { get; set; }

    public bool IsBan => Type == VoteType.Ban;
    public bool IsVote => Kind == SanctionKind.Vote;

    public string KindLabel => Kind switch
    {
        SanctionKind.Auto => "auto",
        SanctionKind.Deferred => "deferred",
        _ => "vote"
    };

    // Distinct, saturated pill colors that read on both light and dark themes.
    public IBrush KindBrush => Kind switch
    {
        SanctionKind.Auto => new SolidColorBrush(Color.FromRgb(0xD9, 0x53, 0x4F)),
        SanctionKind.Deferred => new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)),
        _ => new SolidColorBrush(Color.FromRgb(0xE8, 0xA3, 0x3D))
    };

    /// <summary>Shown for non-vote sanctions in place of the vote counts.</summary>
    public string DetailText => _detail;

    public string SanctionText => Type switch
    {
        VoteType.Mute => "muted 10min",
        _ => _banDuration switch
        {
            BanDuration.Day => "banned 1d",
            BanDuration.Permanent => "banned perm",
            _ => "banned 1h"
        }
    };

    public string UndoText => IsBan ? "Unban" : "Unmute";

    // Permanent bans never expire; timed sanctions match the applied duration
    // (votes: BanDuration.Hour / MuteMinutes = 10; deferred/auto: the stored duration).
    public bool HasExpiry => !(IsBan && _banDuration == BanDuration.Permanent);

    public DateTime ExpiresAt => Type switch
    {
        VoteType.Mute => Timestamp.AddMinutes(10),
        _ => _banDuration switch
        {
            BanDuration.Day => Timestamp.AddDays(1),
            BanDuration.Permanent => DateTime.MaxValue,
            _ => Timestamp.AddMinutes(60)
        }
    };
}
