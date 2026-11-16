namespace VpnHood.App.StoreScreenshots;

// The pill an iPhone wears in its status bar. An iPad has none, and an Android has a punch hole
// instead (DevicePunchHole) - a mockup wearing the wrong one is the tell that nobody checked it.
internal sealed class DeviceIsland
{
    public required double Width { get; init; }
    public required double Height { get; init; }
}
