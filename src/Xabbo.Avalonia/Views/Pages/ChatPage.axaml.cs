using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Xabbo.ViewModels;

namespace Xabbo.Avalonia.Views;

public partial class ChatPage : UserControl
{
    private ScrollViewer? _scrollViewer;
    private bool _wasAtBottom;

    public ChatPage()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is ChatPageViewModel vm)
            {
                vm.OpenHistoryFlyoutAction = () => HistoryButton?.Flyout?.ShowAt(HistoryButton);
                vm.ScrollToMessageAction = ScrollToMessage;
            }
        };

        ListBoxMessages.TemplateApplied += (s, e) =>
        {
            _scrollViewer = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
        };

        AttachedToVisualTree += (s, e) =>
        {
            if (this.FindAncestorOfType<Window>() is { } window)
            {
                window.Deactivated += OnWindowDeactivated;
                window.Activated += OnWindowActivated;
            }
        };

        DetachedFromVisualTree += (s, e) =>
        {
            if (this.FindAncestorOfType<Window>() is { } window)
            {
                window.Deactivated -= OnWindowDeactivated;
                window.Activated -= OnWindowActivated;
            }
        };
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (_scrollViewer is null) return;
        _wasAtBottom = _scrollViewer.Offset.Y + _scrollViewer.Viewport.Height >= _scrollViewer.Extent.Height - 10;
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (_scrollViewer is null || !_wasAtBottom) return;

        // If was at bottom, scroll back to bottom after Avalonia's focus restoration
        Dispatcher.UIThread.Post(() =>
        {
            var bottom = _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height;
            _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, Math.Max(0, bottom));
        }, DispatcherPriority.Loaded);
    }

    private void ScrollToMessage(ChatLogEntryViewModel message)
    {
        // Wait for filters to be applied before scrolling
        Dispatcher.UIThread.Post(() =>
        {
            ListBoxMessages?.ScrollIntoView(message);
        }, DispatcherPriority.Background);
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

        // Update "Remove from filter" submenu
        UpdateRemoveFromFilterSubMenu(chatViewModel, selectedMessages);
    }

    private void UpdateRemoveFromFilterSubMenu(ChatPageViewModel chatViewModel, List<ChatMessageViewModel> selectedMessages)
    {
        // Clear existing items
        RemoveFromFilterSubMenu.ItemsSource = null;

        // Get unique matched words from all selected messages
        var matchedWords = selectedMessages
            .Where(m => m.MatchedWords != null)
            .SelectMany(m => m.MatchedWords!)
            .Distinct()
            .OrderBy(w => w)
            .ToList();

        if (matchedWords.Count == 0)
        {
            RemoveFromFilterSubMenu.IsVisible = false;
            return;
        }

        // Create menu items for each matched word
        var menuItems = new List<MenuFlyoutItem>();
        foreach (var word in matchedWords)
        {
            var menuItem = new MenuFlyoutItem
            {
                Text = word,
                Command = chatViewModel.RemoveFromProfanityFilterCmd,
                CommandParameter = word
            };
            menuItems.Add(menuItem);
        }

        RemoveFromFilterSubMenu.ItemsSource = menuItems;
        RemoveFromFilterSubMenu.IsVisible = true;
    }
}