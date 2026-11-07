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
    private const double BottomBandHeight = 64;

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsEnabled", typeof(ScrollEdgeFade));

    // With IsEnabled, the bottom edge too, for a list that has to show it goes on: what lies below fades
    // out over a taller band, which shrinks away as the end comes into sight.
    public static readonly AttachedProperty<bool> IsBottomEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsBottomEnabled", typeof(ScrollEdgeFade));

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

    public static bool GetIsBottomEnabled(ScrollViewer scroller)
    {
        return scroller.GetValue(IsBottomEnabledProperty);
    }

    public static void SetIsBottomEnabled(ScrollViewer scroller, bool value)
    {
        scroller.SetValue(IsBottomEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(ScrollViewer scroller, AvaloniaPropertyChangedEventArgs e)
    {
        scroller.ScrollChanged -= OnScrollChanged;
        if (e.NewValue is true)
            scroller.ScrollChanged += OnScrollChanged;
        else
            Apply(scroller, 0, 0);
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroller)
            return;

        var bottomBand = GetIsBottomEnabled(scroller)
            ? Math.Min(Math.Round(scroller.ScrollBarMaximum.Y - scroller.Offset.Y), BottomBandHeight)
            : 0;
        Apply(scroller, Math.Min(Math.Round(scroller.Offset.Y), BandHeight), bottomBand);
    }

    // On the content's presenter, so the scroll bar beside it stays whole. The scroller's own is the
    // first on the way down; an inner scroller's lies below it.
    private static void Apply(ScrollViewer scroller, double band, double bottomBand)
    {
        if (scroller.GetVisualDescendants().OfType<ScrollContentPresenter>().FirstOrDefault() is not { } presenter)
            return;

        var height = presenter.Bounds.Height;
        if (bottomBand > 0 && height > 0) {
            presenter.OpacityMask = new LinearGradientBrush {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(0, height, RelativeUnit.Absolute),
                GradientStops = {
                    new GradientStop(band > 0 ? Colors.Transparent : Colors.Black, 0),
                    new GradientStop(Colors.Black, band / height),
                    new GradientStop(Colors.Black, 1 - bottomBand / height),
                    new GradientStop(Colors.Transparent, 1)
                }
            };
            return;
        }

        var current = (presenter.OpacityMask as LinearGradientBrush)?.EndPoint.Point.Y ?? 0;
        if (Math.Abs(current - band) < 0.5) // both in whole pixels
            return;

        presenter.OpacityMask = band == 0 ? null : new LinearGradientBrush {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(0, band, RelativeUnit.Absolute),
            GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 1) }
        };
    }
}
