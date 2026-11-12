using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppUi.Hosting.Desktop.Abstractions;

/// <summary>
/// Represents where a desktop UI's window opens and the room it gives what it shows, in the screen's logical units.
/// </summary>
/// <param name="Left">The left of the window's frame.</param>
/// <param name="Top">The top of the window's frame.</param>
/// <param name="ContentSize">The size inside the frame.</param>
public readonly record struct DesktopWindowPlacement(double Left, double Top, VhSize ContentSize);
