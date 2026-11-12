using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppUi.Hosting.Desktop.Abstractions;

/// <summary>Provides where a desktop UI's window opens, and at what size.</summary>
/// <remarks>
/// The window hands over its screen's work area, its frame (the title bar and the borders) and its UI's shape, in the
/// screen's logical units: a Win32 window's pixels over its scale, an Avalonia window's logical pixels. It is centred in
/// the work area, its top never above the work area's.
/// </remarks>
public static class DesktopWindowFit
{
    // Android TV lays out at 960x540 dp - a 1920x1080 panel at xhdpi, a 4K one at twice the density -
    // and a dp is a web view's CSS px and Avalonia's logical px. A desktop's window takes this size to
    // simulate a TV; an actual TV has no window: the view fills the screen, whatever its size.
    private static readonly VhSize TvSize = new(960, 540);

    // the room a phone-shaped window wants above and below it, which neither a 1366x768 laptop nor
    // 1080p at 150% has
    private const double PortraitMargin = 40;

    /// <summary>Places a window on its screen.</summary>
    /// <param name="workArea">The screen's work area.</param>
    /// <param name="frame">The size the window's frame adds: the title bar and the borders.</param>
    /// <param name="options">The UI's shape.</param>
    /// <param name="isTv"><see langword="true"/> to take a TV's size, 960 by 540, as a desktop simulating a TV does.</param>
    /// <returns>The window's place, and the size inside its frame.</returns>
    /// <remarks>
    /// A TV takes 960 by 540 as it is. Otherwise the window takes the phone's shape where the work area is tall enough for
    /// it with 40 units above and below, whatever its width; else a landscape window whose content is as wide as the UI's
    /// landscape width within the work area, and as tall as a TV's 540 within the work area, though never below the UI's
    /// least landscape height. The window is centred in the work area, its top never above the work area's.
    /// </remarks>
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
