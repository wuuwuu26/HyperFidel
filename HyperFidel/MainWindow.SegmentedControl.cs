using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HyperFidel.Controls;

namespace HyperFidel;

/// <summary>
/// Adds the shared sliding gold thumb to the main window's segmented
/// RadioButton groups.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Wraps a segmented container in a grid with a sliding accent thumb.</summary>
    /// <param name="container">The segmented control's outer border.</param>
    private void AttachSegmentedThumb(Border container)
    {
        if (container.Child is not StackPanel stack)
            return;
        var thumb = new Border
        {
            Background = FindResource<IBrush>("AppAccentBrush"),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false
        };
        var transform = new TranslateTransform();
        thumb.RenderTransform = transform;
        // The stack is still attached to the container here. Adding it to the
        // grid before replacing the container's child would fail validation
        // because a visual can only have one parent; detach it first.
        container.Child = null;
        var grid = new Grid();
        grid.Children.Add(thumb);
        grid.Children.Add(stack);
        container.Child = grid;
        var state = new SegmentedThumbState(container, grid, stack, thumb, transform);
        foreach (var button in stack.Children.OfType<RadioButton>())
            button.IsCheckedChanged += (_, _) => UpdateSegmentedThumb(state, animate: true);
        container.LayoutUpdated += (_, _) => UpdateSegmentedThumb(state, animate: false);
        Dispatcher.UIThread.Post(
            () => UpdateSegmentedThumb(state, animate: false),
            DispatcherPriority.Loaded);
    }

    /// <summary>Moves and resizes the accent thumb to the checked radio button.</summary>
    /// <param name="state">Segmented control state.</param>
    /// <param name="animate">Whether the movement should be animated.</param>
    private void UpdateSegmentedThumb(SegmentedThumbState state, bool animate)
    {
        if (!state.Container.IsVisible)
            return;
        var selected = state.Stack.Children
            .OfType<RadioButton>()
            .FirstOrDefault(button => button.IsChecked == true);
        if (selected is null || selected.Bounds.Width <= 0)
            return;
        var position = selected.TranslatePoint(new Point(0, 0), state.Grid);
        if (position is null)
            return;
        var x = position.Value.X;
        var width = selected.Bounds.Width;
        if (Math.Abs(state.Thumb.Width - width) < 0.1 &&
            Math.Abs(state.Transform.X - x) < 0.1)
        {
            return;
        }
        state.Thumb.Transitions = animate
            ? MotionPreferences.ResolveTransitions(
                _settings.ReduceMotion,
                new Transitions
                {
                    new DoubleTransition
                    {
                        Property = Layoutable.WidthProperty,
                        Duration = TimeSpan.FromMilliseconds(190),
                        Easing = new CubicEaseOut()
                    },
                    new DoubleTransition
                    {
                        Property = TranslateTransform.XProperty,
                        Duration = TimeSpan.FromMilliseconds(190),
                        Easing = new CubicEaseOut()
                    }
                })
            : null;
        state.Thumb.Width = width;
        state.Transform.X = x;
    }

    /// <summary>Holds the visual parts of one segmented thumb.</summary>
    /// <param name="Container">Outer segmented border.</param>
    /// <param name="Grid">Grid that hosts the thumb and button stack.</param>
    /// <param name="Stack">RadioButton stack.</param>
    /// <param name="Thumb">Accent thumb border.</param>
    /// <param name="Transform">Horizontal thumb translation.</param>
    private sealed record SegmentedThumbState(
        Border Container,
        Grid Grid,
        StackPanel Stack,
        Border Thumb,
        TranslateTransform Transform);
}
