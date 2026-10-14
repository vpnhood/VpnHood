using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppUi.Hosting.Abstractions;

// The shape a desktop UI's window takes on its screen (DesktopWindowFit), in the screen's logical
// units. It is the UI's layout's, so the UI states it: an Avalonia UI its own
// (VpnHoodAvaloniaAppBase.WindowFit); a web UI, which has no .NET side, through the head that
// serves it (WindowsWebViewUi).
public class DesktopWindowFitOptions
{
    // the phone's shape the window opens in where its screen has room for it
    public required VhSize PhoneSize { get; init; }

    // the width of the landscape window a screen with no room for the phone's shape gets instead,
    // which is as tall as the TV's
    public required double LandscapeWidth { get; init; }

    // the least height of that landscape window, on a screen shorter than the TV's
    public required double MinLandscapeHeight { get; init; }
}
