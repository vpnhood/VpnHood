using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

// The top edge of a page's scroller, where its content meets the header: what scrolls up there fades
// out over a short band rather than being cut along a line, as iOS's soft scroll edge does. The band
// grows with the scroll from nothing, so a page at rest shows its content whole; through it shows the
// page's own background, so it has no colour of its own. The theme turns it on (AppTheme.axaml).
public static class ScrollEdgeFade
{
    private const double BandHeight = 24;

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsEnabled", typeof(ScrollEdgeFade));

    static ScrollEdgeFade()
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
        scroller.ScrollChanged -= OnScrollChanged;
        if (e.NewValue is true)
            scroller.ScrollChanged += OnScrollChanged;
        else
            Apply(scroller, 0);
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scroller)
            Apply(scroller, Math.Min(Math.Round(scroller.Offset.Y), BandHeight));
    }

    // On the content's presenter, so the scroll bar beside it stays whole. The scroller's own is the
    // first on the way down; an inner scroller's lies below it.
    private static void Apply(ScrollViewer scroller, double band)
    {
        if (scroller.GetVisualDescendants().OfType<ScrollContentPresenter>().FirstOrDefault() is not { } presenter)
            return;

        var current = (presenter.OpacityMask as LinearGradientBrush)?.EndPoint.Point.Y ?? 0;
        if (current == band)
            return;

        presenter.OpacityMask = band == 0 ? null : new LinearGradientBrush {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(0, band, RelativeUnit.Absolute),
            GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 1) }
        };
    }
}
