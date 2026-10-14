using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppUi.Hosting.Desktop.Abstractions;

// Where a desktop UI's window opens and at what size. The window hands over its screen's work area,
// its frame - the title bar and the borders - and its UI's shape (DesktopWindowFitOptions), in the
// screen's logical units: a Win32 window's pixels over its scale, an Avalonia window's logical pixels.
// It opens in the middle of the work area, its title bar on the screen.
public static class DesktopWindowFit
{
    // Android TV lays out at 960x540 dp - a 1920x1080 panel at xhdpi, a 4K one at twice the density -
    // and a dp is a web view's CSS px and Avalonia's logical px. A desktop's window takes this size to
    // simulate a TV; an actual TV has no window: the view fills the screen, whatever its size.
    private static readonly VhSize TvSize = new(960, 540);

    // the room a phone-shaped window wants above and below it, which neither a 1366x768 laptop nor
    // 1080p at 150% has
    private const double PortraitMargin = 40;

    public static DesktopWindowPlacement Fit(VhRect workArea, VhSize frame, DesktopWindowFitOptions options, bool isTv)
    {
        var size = isTv ? TvSize : FitPhone(workArea, frame, options);
        return new DesktopWindowPlacement(
            Left: workArea.X + (workArea.Width - size.Width - frame.Width) / 2,
            Top: Math.Max(workArea.Y, workArea.Y + (workArea.Height - size.Height - frame.Height) / 2),
            ContentSize: size);
    }

    // The phone's shape where the work area has room for it, else landscape: smaller where the work
    // area is, though never below the UI's least landscape height.
    private static VhSize FitPhone(VhRect workArea, VhSize frame, DesktopWindowFitOptions options)
    {
        if (workArea.Height >= options.PhoneSize.Height + frame.Height + 2 * PortraitMargin)
            return options.PhoneSize;

        return new VhSize(Math.Min(options.LandscapeWidth, workArea.Width - frame.Width),
            Math.Max(Math.Min(TvSize.Height, workArea.Height - frame.Height), options.MinLandscapeHeight));
    }
}
