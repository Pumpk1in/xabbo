using System;
using Avalonia;
using Avalonia.Controls;
using Xabbo.Avalonia.Behaviors;
using Xabbo.ViewModels;

namespace Xabbo.Avalonia.Views;

public partial class ChatPage : UserControl
{
    public ChatPage()
    {
        InitializeComponent();
        
        // Observe changes for AttachedProperty IsScrolledToBottom
        ListBoxMessages.GetObservable(ScrollToBottom.IsScrolledToBottomProperty)
            .Subscribe(isAtBottom =>
            {
                if (isAtBottom && DataContext is ChatPageViewModel vm)
                {
                    vm.OnScrolledToBottom();
                }
            });
    }
}