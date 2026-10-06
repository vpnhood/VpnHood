using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppUi.Hosting.Abstractions;

// Where a desktop UI's window opens and the room it gives what it shows (DesktopWindowFit), in the
// screen's logical units: the corner of the window's frame, and the size inside the frame.
public readonly record struct DesktopWindowPlacement(double Left, double Top, VhSize ContentSize);
