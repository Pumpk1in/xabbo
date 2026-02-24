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
    private bool _expectingExtentChange;

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

        // If extent changed and we're expecting it (new message added), scroll to bottom
        if (_expectingExtentChange && Math.Abs(e.ExtentDelta.Y) > 0.1 && _shouldStayAtBottom)
        {
            ScrollToBottom();
            return;
        }

        // Ignore scroll events triggered by our own programmatic scrolls
        if (_isProgrammaticScroll)
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

        // Only handle Add events - we don't expect Remove events anymore
        if (e.Action != NotifyCollectionChangedAction.Add)
            return;

        // Only scroll to bottom if user wants to stay at bottom
        if (!_shouldStayAtBottom)
            return;

        // Mark that we're expecting extent changes (for multi-line message height calculation)
        _expectingExtentChange = true;

        // Schedule scroll after layout pass completes
        Dispatcher.UIThread.Post(() =>
        {
            ScrollToBottom();

            // Stop expecting extent changes after a delay (message should be fully rendered by then)
            DispatcherTimer.RunOnce(() =>
            {
                _expectingExtentChange = false;
            }, TimeSpan.FromMilliseconds(500));

        }, DispatcherPriority.Loaded);
    }

    private void ScrollToBottom()
    {
        if (AssociatedObject is not { Scroll: IScrollable scrollable })
            return;

        _isProgrammaticScroll = true;
        var newOffset = scrollable.Extent.Height - scrollable.Viewport.Height;
        scrollable.Offset = new Vector(scrollable.Offset.X, Math.Max(0, newOffset));
        _isProgrammaticScroll = false;
    }
}
