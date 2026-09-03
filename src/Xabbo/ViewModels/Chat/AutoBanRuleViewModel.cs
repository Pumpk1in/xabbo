using System.Reactive;
using ReactiveUI;

namespace Xabbo.ViewModels;

/// <summary>A room's auto-ban glob pattern, shown in the moderation panel's admin section.</summary>
public class AutoBanRuleViewModel : ViewModelBase
{
    public string Pattern { get; }
    public ReactiveCommand<Unit, Unit> RemoveCmd { get; }

    public AutoBanRuleViewModel(string pattern, Action onRemove)
    {
        Pattern = pattern;
        RemoveCmd = ReactiveCommand.Create(onRemove);
    }
}
