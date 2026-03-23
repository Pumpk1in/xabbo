using System;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;

namespace Xabbo.Avalonia.Behaviors;

/// <summary>
/// Simple autoscroll behavior:
/// - When user is at bottom, new messages scroll the view to stay at bottom
/// - When user scrolls up to read history, new messages arrive but scroll stays static
/// - When user scrolls back to bottom (manually or via button), autoscroll resumes
/// </summary>
public class AutoScrollBehavior : Behavior<ListBox>
{
    private ScrollViewer? _scrollViewer;
    private bool _shouldStayAtBottom = true;
    private bool _isProgrammaticScroll;
    private bool _scrollPending;
    private DateTime _lastAddTime;

    [RequiresUnreferencedCode("override: This functionality is not compatible with trimming.")]
    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject is not null)
        {
            AssociatedObject.TemplateApplied += OnTemplateApplied;
            AssociatedObject.Items.CollectionChanged += OnCollectionChanged;
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.TemplateApplied -= OnTemplateApplied;
            AssociatedObject.Items.CollectionChanged -= OnCollectionChanged;
        }
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        }
        _scrollViewer = null;

        base.OnDetaching();
    }

    private void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        _scrollViewer = e.NameScope.Get<ScrollViewer>("PART_ScrollViewer");
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += OnScrollChanged;
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scrollViewer is null)
            return;

        // Ignore scroll events triggered by our own programmatic scrolls
        if (_isProgrammaticScroll)
            return;

        // Ignore scroll events shortly after an Add — these are layout adjustments, not user scrolls
        if ((DateTime.UtcNow - _lastAddTime).TotalMilliseconds < 500)
            return;

        // Only react to actual scroll movements (not extent changes)
        if (Math.Abs(e.OffsetDelta.Y) < 0.1)
            return;

        // Update our flag based on whether user is at bottom
        bool isAtBottom = _scrollViewer.Offset.Y + _scrollViewer.Viewport.Height >= (_scrollViewer.Extent.Height - 10);
        _shouldStayAtBottom = isAtBottom;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_scrollViewer is null)
            return;

        // On bulk remove (purge), nudge scroll to force VSP to re-realize items
        if (e.Action == NotifyCollectionChangedAction.Remove || e.Action == NotifyCollectionChangedAction.Reset)
        {
            if (_shouldStayAtBottom)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    NudgeScroll();
                    DispatcherTimer.RunOnce(() => ScrollToBottom(), TimeSpan.FromMilliseconds(100));
                }, DispatcherPriority.Loaded);
            }
            return;
        }

        if (e.Action != NotifyCollectionChangedAction.Add)
            return;

        // Only scroll to bottom if user wants to stay at bottom
        if (!_shouldStayAtBottom)
            return;

        // Mark the time so OnScrollChanged ignores layout-induced scroll events
        _lastAddTime = DateTime.UtcNow;

        // Coalesce: skip if a scroll is already pending
        if (_scrollPending)
            return;
        _scrollPending = true;

        // Scroll to bottom after layout, with one retry if not fully scrolled
        Dispatcher.UIThread.Post(() =>
        {
            _scrollPending = false;
            ScrollToBottom();
            if (!IsAtBottom())
                DispatcherTimer.RunOnce(() => ScrollToBottom(), TimeSpan.FromMilliseconds(100));
        }, DispatcherPriority.Loaded);
    }

    private void NudgeScroll()
    {
        if (AssociatedObject is not { Scroll: IScrollable scrollable })
            return;

        _isProgrammaticScroll = true;
        scrollable.Offset = new Vector(scrollable.Offset.X, Math.Max(0, scrollable.Offset.Y - 50));
        _isProgrammaticScroll = false;
    }

    private bool IsAtBottom()
    {
        if (_scrollViewer is null) return true;
        return _scrollViewer.Offset.Y + _scrollViewer.Viewport.Height >= (_scrollViewer.Extent.Height - 10);
    }

    private void ScrollToBottom()
    {
        if (AssociatedObject is null || AssociatedObject.Items.Count == 0)
            return;

        _isProgrammaticScroll = true;
        // ScrollIntoView forces realization of the last item
        var lastItem = AssociatedObject.Items[AssociatedObject.Items.Count - 1];
        AssociatedObject.ScrollIntoView(lastItem!);
        // Then push to absolute bottom (including list padding)
        if (AssociatedObject.Scroll is IScrollable scrollable)
        {
            var maxOffset = scrollable.Extent.Height - scrollable.Viewport.Height;
            scrollable.Offset = new Vector(scrollable.Offset.X, Math.Max(0, maxOffset));
        }
        _isProgrammaticScroll = false;
    }
}
