namespace VpnHood.AppLib.Api.Device;

/// <summary>Represents the heights of the system bars over the app's window, in the device's pixels.</summary>
public class SystemBarsInfo
{
    /// <summary>Gets the bars of no height, for a device that has none or cannot tell them.</summary>
    public static SystemBarsInfo Default { get; } = new() { TopHeight = 0, BottomHeight = 0 };

    /// <summary>Gets the height of the bar at the top, as the status bar.</summary>
    public required int TopHeight { get; init; }

    /// <summary>Gets the height of the bar at the bottom, as the navigation bar.</summary>
    public required int BottomHeight { get; init; }
}