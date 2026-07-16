using System.Globalization;

namespace VpnHood.AppLib.Abstractions;

/// <summary>
/// Same as RegionInfo.CurrentRegion, but settable. The client country is never discovered
/// (no IP lookup, no VPN server report); a wrong locale-based region is tolerable because it
/// only selects default policies. Assign CurrentRegion to override, or call Reset to restore
/// the device region.
/// </summary>
public static class AppRegionInfo
{
    private static RegionInfo? _currentRegion;

    public static RegionInfo CurrentRegion {
        get => _currentRegion ?? RegionInfo.CurrentRegion;
        set => _currentRegion = value;
    }

    public static void Reset()
    {
        _currentRegion = null;
    }
}
