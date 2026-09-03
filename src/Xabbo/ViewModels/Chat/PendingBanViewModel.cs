using System.Reactive;
using ReactiveUI;

namespace Xabbo.ViewModels;

/// <summary>A deferred exact-name ban queued for its target's next room entry, shown in the moderation panel.</summary>
public class PendingBanViewModel : ViewModelBase
{
    public string Name { get; }
    public string DurationText { get; }
    public ReactiveCommand<Unit, Unit> CancelCmd { get; }

    public PendingBanViewModel(string name, string durationText, Action onCancel)
    {
        Name = name;
        DurationText = durationText;
        CancelCmd = ReactiveCommand.Create(onCancel);
    }
}
