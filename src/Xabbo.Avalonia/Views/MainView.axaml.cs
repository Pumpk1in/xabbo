using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using ReactiveUI;

using Xabbo.ViewModels;

namespace Xabbo.Avalonia.Views;

public partial class MainView : UserControl
{
    public MainViewModel? ViewModel => DataContext as MainViewModel;

    public MainView()
    {
        InitializeComponent();

        NavView.ItemInvoked += NavView_ItemInvoked;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        Dispatcher.UIThread.Post(AttachInfoBadges, DispatcherPriority.Background);
    }

    private void NavView_ItemInvoked(object? sender, NavigationViewItemInvokedEventArgs e)
    {
        if (ViewModel is null) return;

        if (e.InvokedItemContainer.DataContext is PageViewModel pageVm)
            ViewModel.SelectedPage = pageVm;
    }

    private void AttachInfoBadges()
    {
        if (ViewModel is null) return;

        foreach (var page in ViewModel.Pages)
        {
            if (page is not MessagesPageViewModel messagesVm) continue;

            var navItems = NavView.GetVisualDescendants().OfType<NavigationViewItem>();
            foreach (var navItem in navItems)
            {
                if (navItem.DataContext != messagesVm) continue;

                var badge = new InfoBadge { Value = messagesVm.TotalUnread, IsVisible = messagesVm.TotalUnread > 0 };
                navItem.InfoBadge = badge;

                messagesVm.WhenAnyValue(x => x.TotalUnread)
                    .Subscribe(count =>
                    {
                        badge.Value = count;
                        badge.IsVisible = count > 0;
                    });

                return;
            }
        }
    }
}
