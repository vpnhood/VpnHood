using Avalonia;
using Avalonia.Controls;
using VpnHood.AppUi.Hosting.Abstractions;
using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

// The desktop window at the size and place DesktopWindowFit gives its screen, before it opens, from
// its frame as far as it is known then: Windows knows it; X11 only once the window manager has
// framed the window, after it opened, when the window is fitted again. And again whenever a screen
// changes, as a new resolution or a remote session's resized window changes it.
internal static class AvaloniaWindowFit
{
    public static void Apply(Window window, VhSize phoneSize, bool isTv)
    {
        Fit(window, phoneSize, isTv, isRefit: false, isPlaced: false);

        // the frame goes from unknown to known only once, and the window manager has placed the
        // window by then
        window.PropertyChanged += (_, e) => {
            if (e.Property == TopLevel.FrameSizeProperty && e.OldValue is null && e.NewValue is not null)
                Fit(window, phoneSize, isTv, isRefit: true, isPlaced: true);
        };

        // The window may be off a screen that shrank, so it goes back to the middle; a minimized one
        // stays, its frame its button's.
        window.Screens.Changed += (_, _) => {
            if (window.WindowState != WindowState.Minimized)
                Fit(window, phoneSize, isTv, isRefit: true, isPlaced: false);
        };
    }

    // A placed window moves only with a size that changes. The window manager keeps a window it moves
    // inside the work area at the size the window has at that moment, so a refit's new size goes to
    // the platform first, or GNOME stops a window about to shrink where its old size still fits.
    private static void Fit(Window window, VhSize phoneSize, bool isTv, bool isRefit, bool isPlaced)
    {
        if ((window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary) is not { } screen)
            return;

        var workArea = screen.WorkingArea.ToRect(screen.Scaling);
        var placement = DesktopWindowFit.Fit(new VhRect(workArea.X, workArea.Y, workArea.Width, workArea.Height),
            FrameOf(window), phoneSize, isTv);
        var size = placement.ContentSize;
        if (isPlaced && size == new VhSize(window.Width, window.Height))
            return;

        window.Width = size.Width;
        window.Height = size.Height;
        if (isRefit)
            window.UpdateLayout();
        window.Position = PixelPoint.FromPoint(new Point(placement.Left, placement.Top), screen.Scaling);
    }

    // The title bar and the borders, as far as they are known: none before X11's window manager has
    // framed the window.
    private static VhSize FrameOf(Window window)
    {
        return window.FrameSize is { } frameSize
            ? new VhSize(frameSize.Width - window.ClientSize.Width, frameSize.Height - window.ClientSize.Height)
            : default;
    }
}
