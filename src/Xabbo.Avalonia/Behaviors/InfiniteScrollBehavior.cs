using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;

namespace Xabbo.Avalonia.Behaviors;

/// Handles history loading when scrolling up and compensates for scroll anchoring
/// to prevent visual jumps.

public class InfiniteScrollBehavior : Behavior<ListBox>
{
    private ScrollViewer? _scrollViewer;
    private bool _isLoading;

    // Infinite auto-scroll property
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<InfiniteScrollBehavior, ICommand?>(nameof(Command));

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public static void SetCommand(AvaloniaObject element, ICommand? value) => element.SetValue(CommandProperty, value);

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            AssociatedObject.TemplateApplied += OnTemplateApplied;
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (AssociatedObject != null)
        {
            AssociatedObject.TemplateApplied -= OnTemplateApplied;
        }
        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer = null;
        }
    }

    // Use of 'global::' to avoid namespace conflict
    private void OnTemplateApplied(object? sender, global::Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        _scrollViewer = e.NameScope.Get<ScrollViewer>("PART_ScrollViewer");
        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged += OnScrollChanged;
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        
        if (_isLoading || _scrollViewer == null || AssociatedObject == null) return;

        if (_scrollViewer.Offset.Y < 20 && _scrollViewer.Extent.Height > _scrollViewer.Viewport.Height)
        {
            var command = Command;
            if (command != null && command.CanExecute(null))
            {
                LoadMoreItems(command);
            }
        }
    }

    private void LoadMoreItems(ICommand command)
    {
        _isLoading = true;

        // Capture current height BEFORE adding items
        double oldExtentHeight = _scrollViewer!.Extent.Height;
        double oldOffset = _scrollViewer.Offset.Y;

        // Execute command (Adds items to ViewModel)
        command.Execute(null);

        // Wait for layout update
        Dispatcher.UIThread.Post(() =>
        {
            if (AssociatedObject == null || _scrollViewer == null) return;

            // Force layout update to get the real new height
            AssociatedObject.UpdateLayout();

            // Capture new height
            double newExtentHeight = _scrollViewer.Extent.Height;

            // Calculate difference (height of items added at the top)
            double heightDifference = newExtentHeight - oldExtentHeight;

            if (heightDifference > 0)
            {
                // ADJUST SCROLL (Scroll Anchoring)
                // Add difference to current offset.
                // Creates illusion that user hasn't moved, while content was inserted above.
                _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, oldOffset + heightDifference);
            }

            _isLoading = false;
            
        }, DispatcherPriority.ContextIdle);
    }
}