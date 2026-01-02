using System;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;

namespace Xabbo.Avalonia.Behaviors;

public class AutoScrollBehavior : Behavior<ListBox>
{
    private ScrollViewer? _scrollViewer;
    private double _previousExtentHeight;
    private bool _isLoadingHistory;
    private bool _shouldStayAtBottom;
    private bool _pendingScrollToBottom;

    [RequiresUnreferencedCode("override: This functionality is not compatible with trimming.")]
    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject is not null)
        {
            AssociatedObject.TemplateApplied += AssociatedObjectOnTemplateApplied;
            AssociatedObject.Items.CollectionChanged += OnCollectionChanged;
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.TemplateApplied -= AssociatedObjectOnTemplateApplied;
            AssociatedObject.Items.CollectionChanged -= OnCollectionChanged;
        }
        _scrollViewer = null;

        base.OnDetaching();
    }

    private void AssociatedObjectOnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        // Get the inner ScrollViewer control
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

        // Check if user is at bottom
        bool isAtBottom = _scrollViewer.Offset.Y + _scrollViewer.Viewport.Height >= (_scrollViewer.Extent.Height - 10);

        // Update our flag - if user manually scrolls away from bottom, disable auto-scroll
        if (Math.Abs(e.OffsetDelta.Y) > 0.1) // User initiated scroll (not programmatic)
        {
            _shouldStayAtBottom = isAtBottom;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_scrollViewer is null)
            return;

        // Handle Reset action (when collection is rebuilt, e.g., during history loading or buffer cleanup)
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            bool wasAtBottom = _scrollViewer.Offset.Y + _scrollViewer.Viewport.Height >= (_scrollViewer.Extent.Height - 10);

            _isLoadingHistory = _scrollViewer.Offset.Y < 100; // If near top, we're loading history
            _previousExtentHeight = _scrollViewer.Extent.Height;

            // If we were at bottom before reset, make sure we stay there after
            if (wasAtBottom || _shouldStayAtBottom)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (AssociatedObject is { Scroll: IScrollable scrollable })
                    {
                        var newOffset = scrollable.Extent.Height - scrollable.Viewport.Height;
                        scrollable.Offset = new Vector(scrollable.Offset.X, Math.Max(0, newOffset));
                    }
                }, DispatcherPriority.ContextIdle);
            }
            return;
        }

        // Handle Remove action (when items are removed from top during buffer cleanup)
        if (e.Action == NotifyCollectionChangedAction.Remove)
        {
            bool wasAtBottom = _scrollViewer.Offset.Y + _scrollViewer.Viewport.Height >= (_scrollViewer.Extent.Height - 10);

            // If we should stay at bottom, schedule a single scroll after all removes are done
            if ((wasAtBottom || _shouldStayAtBottom) && !_pendingScrollToBottom)
            {
                _pendingScrollToBottom = true;
                Dispatcher.UIThread.Post(() =>
                {
                    _pendingScrollToBottom = false;
                    if (AssociatedObject is { Scroll: IScrollable scrollable })
                    {
                        var newOffset = scrollable.Extent.Height - scrollable.Viewport.Height;
                        scrollable.Offset = new Vector(scrollable.Offset.X, Math.Max(0, newOffset));
                    }
                }, DispatcherPriority.ContextIdle);
            }
            return;
        }

        if (e.Action != NotifyCollectionChangedAction.Add)
            return;

        // Check if the user was scrolled to the bottom BEFORE the content change.
        bool wasAtBottom2 = _scrollViewer.Offset.Y + _scrollViewer.Viewport.Height >= (_scrollViewer.Extent.Height - 10);

        if (wasAtBottom2 || _shouldStayAtBottom)
        {
            _shouldStayAtBottom = true; // Keep staying at bottom

            // Schedule the scroll to happen AFTER the layout pass is complete.
            // Fix for multi-line items: ensures the ScrollViewer.Extent.Height has been updated to the item's final size.
            Dispatcher.UIThread.Post(() =>
            {
                if (AssociatedObject is { Scroll: IScrollable scrollable })
                {
                    var newOffset = scrollable.Extent.Height - scrollable.Viewport.Height;
                    scrollable.Offset = new Vector(scrollable.Offset.X, newOffset);

                    // Double-check after a short delay to handle multi-line messages whose height is still being calculated
                    DispatcherTimer.RunOnce(() =>
                    {
                        if (AssociatedObject is { Scroll: IScrollable scrollable2 })
                        {
                            var currentBottom = scrollable2.Offset.Y + scrollable2.Viewport.Height;
                            var expectedBottom = scrollable2.Extent.Height;

                            // If we're not at the bottom (with 10px tolerance), scroll again
                            if (currentBottom < expectedBottom - 10)
                            {
                                var correctedOffset = scrollable2.Extent.Height - scrollable2.Viewport.Height;
                                scrollable2.Offset = new Vector(scrollable2.Offset.X, correctedOffset);
                            }
                        }
                    }, TimeSpan.FromMilliseconds(100));
                }
            }, DispatcherPriority.Loaded);
        }
        else if (_isLoadingHistory && e.NewItems?.Count > 0)
        {
            // We're loading history at the top - preserve scroll position
            var currentOffset = _scrollViewer.Offset.Y;
            Dispatcher.UIThread.Post(() =>
            {
                if (AssociatedObject is { Scroll: IScrollable scrollable })
                {
                    var heightDelta = scrollable.Extent.Height - _previousExtentHeight;
                    var newOffset = currentOffset + heightDelta;
                    scrollable.Offset = new Vector(scrollable.Offset.X, Math.Max(0, newOffset));
                    _isLoadingHistory = false;
                }
            }, DispatcherPriority.ContextIdle);
        }
    }

    // OnScrollChanged, _isScrolling, and _scrollOnNextChange flags are removed as they are no longer necessary with the Dispatcher-based approach.
}