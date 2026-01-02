using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not ChatPageViewModel chatViewModel)
            return;

        var selectedMessages = ListBoxMessages
            .SelectedItems?
            .OfType<ChatMessageViewModel>()
            .ToList() ?? new List<ChatMessageViewModel>();

        if (selectedMessages.Count == 0 && e.Source is Control control)
        {
            var listBoxItem = control.FindAncestorOfType<ListBoxItem>();
            if (listBoxItem?.DataContext is ChatMessageViewModel message)
            {
                selectedMessages = new List<ChatMessageViewModel> { message };
            }
        }

        chatViewModel.ContextSelection = selectedMessages.ToList();
    }
}