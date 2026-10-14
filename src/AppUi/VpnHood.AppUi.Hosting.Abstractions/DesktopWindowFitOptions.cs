using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppUi.Hosting.Abstractions;

// The shape a desktop UI's window takes on its screen (DesktopWindowFit), which is the UI's layout's:
// a head names it on the UI it runs. The defaults are the classic UI's, in the screen's logical units.
public class DesktopWindowFitOptions
{
    // the phone's shape the window opens in where its screen has room for it
    public VhSize PhoneSize { get; init; } = new(400, 700);

    // A phone-shaped window with no room on its screen opens in landscape instead, as tall as the
    // TV's and only as wide as the classic UI's home needs to show its rows whole in every language
    // (Client's "no server selected" in Spanish and Portuguese is the longest), which also puts them
    // beside the circle, as it does over 600.
    public double LandscapeWidth { get; init; } = 800;

    // the least a landscape window gets: the classic UI's home with the countdown takes about 380,
    // the plain one breaks at 340
    public double MinLandscapeHeight { get; init; } = 400;
}
