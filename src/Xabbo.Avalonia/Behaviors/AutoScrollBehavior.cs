using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading; // Important: Added for Dispatcher
using Avalonia.Xaml.Interactivity;

namespace Xabbo.Avalonia.Behaviors;

public class AutoScrollBehavior : Behavior<ListBox>
{
    private ScrollViewer? _scrollViewer;

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
        // ScrollChanged is no longer subscribed to as we initiate the scroll after layout, preventing the race condition.
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || _scrollViewer is null)
            return;

        // Check if the user was scrolled to the bottom BEFORE the content change.
        // (current visible bottom) >= (total height - tolerance).
        bool wasAtBottom = _scrollViewer.Offset.Y + _scrollViewer.Viewport.Height >= (_scrollViewer.Extent.Height - 10);

        if (wasAtBottom)
        {
            // Schedule the scroll to happen AFTER the layout pass is complete (DispatcherPriority.Layout).
            // Fix for multi-line items. It ensures the ScrollViewer.Extent.Height has been updated to the item's final size before attempting to scroll.
            Dispatcher.UIThread.Post(() =>
            {
                if (AssociatedObject is { Scroll: IScrollable scrollable })
                {
                    scrollable.Offset = new Vector(
                        scrollable.Offset.X,
                        scrollable.Extent.Height - scrollable.Viewport.Height);
                }
            }, DispatcherPriority.ContextIdle);
        }
    }

    // OnScrollChanged, _isScrolling, and _scrollOnNextChange flags are removed as they are no longer necessary with the Dispatcher-based approach.
}