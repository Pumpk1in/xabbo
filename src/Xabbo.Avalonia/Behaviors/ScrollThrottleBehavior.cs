using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;

namespace Xabbo.Avalonia.Behaviors;

/// <summary>
/// Hides expensive UI elements during any scroll, except when at the bottom (autoscroll mode).
/// Shows elements again after 300ms of no scrolling.
/// </summary>
public class ScrollThrottleBehavior : Behavior<ListBox>
{
    public static readonly AttachedProperty<bool> IsThrottledProperty =
        AvaloniaProperty.RegisterAttached<ScrollThrottleBehavior, ListBox, bool>("IsThrottled");

    public static bool GetIsThrottled(ListBox element) => element.GetValue(IsThrottledProperty);
    public static void SetIsThrottled(ListBox element, bool value) => element.SetValue(IsThrottledProperty, value);

    private ScrollViewer? _scrollViewer;
    private DispatcherTimer? _unthrottleTimer;

    [RequiresUnreferencedCode("override: This functionality is not compatible with trimming.")]
    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is not null)
        {
            AssociatedObject.TemplateApplied += OnTemplateApplied;
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.TemplateApplied -= OnTemplateApplied;
        }
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        }
        _unthrottleTimer?.Stop();
        base.OnDetaching();
    }

    private void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        _scrollViewer = e.NameScope.Get<ScrollViewer>("PART_ScrollViewer");
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += OnScrollChanged;
        }

        _unthrottleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _unthrottleTimer.Tick += (_, _) =>
        {
            _unthrottleTimer.Stop();
            if (AssociatedObject is not null)
            {
                SetIsThrottled(AssociatedObject, false);
            }
        };
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scrollViewer is null || AssociatedObject is null)
            return;

        // Ignore extent changes (new items added) - only react to actual scroll movements
        if (Math.Abs(e.OffsetDelta.Y) < 0.1)
            return;

        // Check if we're at the bottom (autoscroll mode) - don't throttle
        bool isAtBottom = _scrollViewer.Offset.Y + _scrollViewer.Viewport.Height >= _scrollViewer.Extent.Height - 10;
        if (isAtBottom)
        {
            // At bottom = autoscroll mode, don't throttle, and ensure we're not throttled
            _unthrottleTimer?.Stop();
            SetIsThrottled(AssociatedObject, false);
            return;
        }

        // User is scrolling and not at bottom - throttle
        SetIsThrottled(AssociatedObject, true);

        // Reset the debounce timer
        _unthrottleTimer?.Stop();
        _unthrottleTimer?.Start();
    }
}
