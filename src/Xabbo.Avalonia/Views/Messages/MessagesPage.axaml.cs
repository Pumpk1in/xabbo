using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using ReactiveUI;
using Xabbo.ViewModels;

namespace Xabbo.Avalonia.Views;

public partial class MessagesPage : UserControl
{
    public MessagesPage()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MessagesPageViewModel vm)
            {
                vm.WhenAnyValue(x => x.SelectedConversation)
                    .Subscribe(_ => ScrollMessagesToBottom());
            }
        };
    }

    private void ScrollMessagesToBottom()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (MessagesList.Scroll is IScrollable scrollable)
            {
                var newOffset = scrollable.Extent.Height - scrollable.Viewport.Height;
                scrollable.Offset = new global::Avalonia.Vector(scrollable.Offset.X, Math.Max(0, newOffset));
            }
        }, DispatcherPriority.Loaded);
    }
}
