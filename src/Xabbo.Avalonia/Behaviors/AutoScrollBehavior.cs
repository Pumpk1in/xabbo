using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Xaml.Interactivity;

namespace Xabbo.Avalonia.Behaviors;

public class AutoScrollBehavior : Behavior<ListBox>
{
    private ScrollViewer? _scrollViewer;
    private bool _isScrolling;
    private bool _scrollOnNextChange;

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

    private void AssociatedObjectOnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        _scrollViewer = e.NameScope.Get<ScrollViewer>("PART_ScrollViewer");
        _scrollViewer.ScrollChanged += OnScrollChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_scrollViewer is { } scrollViewer)
        {
            // Check if chat scroll was on the bottom BEFORE the new entry
            bool wasAtBottom = scrollViewer.Offset.Y + scrollViewer.Viewport.Height >= (scrollViewer.Extent.Height - scrollViewer.Viewport.Height - 10);
            
            if (wasAtBottom)
            {
                _scrollOnNextChange = true;
            }
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isScrolling)
            return;

        if (_scrollOnNextChange && _scrollViewer is { } scrollViewer)
        {
            // Check if we have to scroll
            if (scrollViewer.Offset.Y + scrollViewer.Viewport.Height < scrollViewer.Extent.Height - 10)
            {
                if (AssociatedObject is { Scroll: IScrollable scrollable })
                {
                    _isScrolling = true;
                    scrollable.Offset = new Vector(
                        scrollable.Offset.X,
                        scrollable.Extent.Height - scrollable.Viewport.Height);
                    _isScrolling = false;
                }
            }
            _scrollOnNextChange = false;
        }
    }
}