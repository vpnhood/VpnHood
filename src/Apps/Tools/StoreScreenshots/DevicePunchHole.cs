namespace VpnHood.App.StoreScreenshots;

// An Android phone's camera cutout: a hole punched in the status bar, not a pill. Tablets put the
// camera on the long edge and show none.
internal sealed class DevicePunchHole
{
    public required double Size { get; init; }
}
