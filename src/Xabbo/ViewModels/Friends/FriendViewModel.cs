using System.Reactive.Linq;
using ReactiveUI;

namespace Xabbo.ViewModels;

public sealed partial class FriendViewModel : ViewModelBase
{
    public Id Id { get; set; }
    [Reactive] public bool IsOnline { get; set; }
    [Reactive] public string Name { get; set; } = "";
    [Reactive] public string Motto { get; set; } = "";
    [Reactive] public string Figure { get; set; } = "";

    public bool IsOrigins { get; set; }
    [Reactive] public string? ModernFigure { get; set; }
    [Reactive] public bool NotifyWhenOnline { get; set; }

    public string NotifyMenuText => NotifyWhenOnline ? "Stop notifying when online" : "Notify when online";

    public FriendViewModel()
    {
        this.WhenAnyValue(x => x.NotifyWhenOnline)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(NotifyMenuText)));
    }
}
