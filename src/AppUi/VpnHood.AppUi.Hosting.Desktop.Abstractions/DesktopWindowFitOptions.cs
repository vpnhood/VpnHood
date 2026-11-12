using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppUi.Hosting.Desktop.Abstractions;

/// <summary>
/// Represents the shape a desktop UI's window takes on its screen, in the screen's logical units, as the UI's layout
/// states it.
/// </summary>
public class DesktopWindowFitOptions
{
    /// <summary>Gets the phone's shape the window opens in where its screen is tall enough for it.</summary>
    public required VhSize PhoneSize { get; init; }

    /// <summary>Gets the width of the landscape window a screen with no room for the phone's shape gets instead.</summary>
    public required double LandscapeWidth { get; init; }

    /// <summary>Gets the least height of that landscape window.</summary>
    public required double MinLandscapeHeight { get; init; }
}
