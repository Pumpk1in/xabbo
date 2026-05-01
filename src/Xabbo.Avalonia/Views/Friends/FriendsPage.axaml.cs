using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Xabbo.ViewModels;

namespace Xabbo.Avalonia.Views;

public partial class FriendsPage : UserControl
{
    public FriendsPage()
    {
        InitializeComponent();
    }

    private void OnFriendFlyoutOpened(object? sender, System.EventArgs e)
    {
        if (sender is FlyoutBase flyout &&
            flyout.Target?.DataContext is FriendViewModel friend &&
            DataContext is FriendsPageViewModel vm)
        {
            vm.ContextFriend = friend;
        }
    }
}
