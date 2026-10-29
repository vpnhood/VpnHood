using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

// Text with no control in it, which a remote has no other way through: turned on, its scroller takes
// the focus itself, and while it has it Up and Down move it most of its height - a line a press would
// take dozens - keeping the last lines in sight. At either end they fall through to XY focus, which
// walks on to the page's next control. Avalonia's ScrollViewer takes only PageUp and PageDown itself.
public static class ArrowKeyScroll
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsEnabled", typeof(ArrowKeyScroll));

    static ArrowKeyScroll()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(ScrollViewer scroller)
    {
        return scroller.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(ScrollViewer scroller, bool value)
    {
        scroller.SetValue(IsEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(ScrollViewer scroller, AvaloniaPropertyChangedEventArgs e)
    {
        var isEnabled = e.NewValue is true;
        scroller.SetCurrentValue(InputElement.FocusableProperty, isEnabled);
        scroller.KeyDown -= OnKeyDown;
        if (isEnabled)
            scroller.KeyDown += OnKeyDown;
    }

    // the scroller's own keys only: one from a control inside it is that control's
    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ScrollViewer scroller || e.Source != scroller)
            return;

        var step = scroller.Viewport.Height * 0.75;
        var offset = scroller.Offset.Y;
        var end = scroller.ScrollBarMaximum.Y;
        if (e.Key == Key.Up && offset > 0)
            scroller.Offset = scroller.Offset.WithY(Math.Max(offset - step, 0));
        else if (e.Key == Key.Down && offset < end)
            scroller.Offset = scroller.Offset.WithY(Math.Min(offset + step, end));
        else
            return;

        e.Handled = true;
    }
}
