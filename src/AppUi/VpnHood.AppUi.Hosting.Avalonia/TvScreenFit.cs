using Avalonia.Controls;
using Avalonia.Media;

namespace VpnHood.AppUi.Hosting.Avalonia;

// A TV layout is drawn for 960x540, so its type reads from across a room. A TV's own layout size is
// not that everywhere: tvOS lays out in points, 1920x1080, and an Android TV's size comes from a
// density its maker chose, so a set can lay out in 1920x1080 dp, which shows the UI at half its size,
// or in so few that the UI takes a phone's layout and is cut off. This fills the screen as 960x540
// would, whatever the size. A transform rather than other sizes keeps one layout for every TV.
internal sealed class TvScreenFit : LayoutTransformControl
{
    private const double LayoutWidth = 960;
    private const double LayoutHeight = 540;

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        var scale = Math.Min(e.NewSize.Width / LayoutWidth, e.NewSize.Height / LayoutHeight);
        if (scale > 0)
            LayoutTransform = new ScaleTransform(scale, scale);
    }
}
