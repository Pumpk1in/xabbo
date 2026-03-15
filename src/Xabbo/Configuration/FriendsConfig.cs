using ReactiveUI;

namespace Xabbo.Configuration;

public sealed class FriendsConfig : ReactiveObject
{
    [Reactive] public bool NotifyOnline { get; set; } = true;
    public HashSet<long> NotifyOnlineIds { get; set; } = [];
}
