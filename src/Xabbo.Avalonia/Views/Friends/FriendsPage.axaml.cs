using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Xabbo.ViewModels;

namespace Xabbo.Avalonia.Views;

public partial class FriendsPage : UserControl
{
    private const double MinTileWidth = 260;
    private const double TwoColumnThreshold = MinTileWidth * 2 + 20;

    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<FriendsPage, int>(nameof(Columns), 1);

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    private ScrollViewer? _scrollViewer;
    private IDisposable? _viewportSub;

    public FriendsPage()
    {
        InitializeComponent();
        FriendsList.TemplateApplied += OnListTemplateApplied;
    }

    private void OnListTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        _viewportSub?.Dispose();
        _scrollViewer = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
        if (_scrollViewer is not null)
        {
            _viewportSub = _scrollViewer.GetObservable(ScrollViewer.ViewportProperty)
                .Subscribe(vp => UpdateColumns(vp.Width));
            UpdateColumns(_scrollViewer.Viewport.Width);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _viewportSub?.Dispose();
        _viewportSub = null;
        _scrollViewer = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateColumns(double viewportWidth)
    {
        if (viewportWidth <= 0) return;
        Columns = viewportWidth >= TwoColumnThreshold ? 2 : 1;
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
