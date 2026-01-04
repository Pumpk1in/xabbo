using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Xabbo.ViewModels;

namespace Xabbo.Avalonia.Views;

public partial class ChatPage : UserControl
{
    public ChatPage()
    {
        InitializeComponent();
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