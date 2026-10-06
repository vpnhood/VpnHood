using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppUi.Hosting.Abstractions;

// Where a desktop UI's window opens and at what size, the same for every desktop UI. The window
// hands over its screen's work area, its frame - the title bar and the borders - and the phone's
// shape it opens in (AppResources.WindowSize), in the screen's logical units: a WPF window's DIPs,
// an Avalonia window's logical pixels. It opens in the middle of the work area, its title bar on
// the screen.
public static class DesktopWindowFit
{
    // Android TV lays out at 960x540 dp - a 1920x1080 panel at xhdpi, a 4K one at twice the density -
    // and a dp is a web view's CSS px and Avalonia's logical px. A desktop's window takes this size to
    // simulate a TV; an actual TV has no window: the view fills the screen, whatever its size.
    private static readonly VhSize TvSize = new(960, 540);

    // A phone-shaped window with no room on its screen opens in landscape instead, as tall as the
    // TV's and only as wide as the classic UI's home needs to show its rows whole in every language
    // (Client's "no server selected" in Spanish and Portuguese is the longest), which also puts them
    // beside the circle, as it does over 600.
    private const double LandscapeWidth = 800;

    // the least a landscape window gets: the classic UI's home with the countdown takes about 380,
    // the plain one breaks at 340
    private const double MinLandscapeHeight = 400;

    // the room a phone-shaped window wants above and below it, which neither a 1366x768 laptop nor
    // 1080p at 150% has
    private const double PortraitMargin = 40;

    public static DesktopWindowPlacement Fit(VhRect workArea, VhSize frame, VhSize phoneSize, bool isTv)
    {
        var size = isTv ? TvSize : FitPhone(workArea, frame, phoneSize);
        return new DesktopWindowPlacement(
            Left: workArea.X + (workArea.Width - size.Width - frame.Width) / 2,
            Top: Math.Max(workArea.Y, workArea.Y + (workArea.Height - size.Height - frame.Height) / 2),
            ContentSize: size);
    }

    // The phone's shape where the work area has room for it, else landscape: smaller where the work
    // area is, though never below MinLandscapeHeight.
    private static VhSize FitPhone(VhRect workArea, VhSize frame, VhSize phoneSize)
    {
        if (workArea.Height >= phoneSize.Height + frame.Height + 2 * PortraitMargin)
            return phoneSize;

        return new VhSize(Math.Min(LandscapeWidth, workArea.Width - frame.Width),
            Math.Max(Math.Min(TvSize.Height, workArea.Height - frame.Height), MinLandscapeHeight));
    }
}
