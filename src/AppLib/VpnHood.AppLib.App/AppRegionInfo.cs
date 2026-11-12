using System.Globalization;

namespace VpnHood.AppLib.App;

/// <summary>
/// Provides the app's region, which picks the region's defaults, such as whether usage data waits for a yes first,
/// and the ads' countries: the device's own (<see cref="RegionInfo.CurrentRegion"/>) until it is set.
/// </summary>
/// <remarks>
/// The app sets it at its start and on each settings change from the country a VPN server last reported; it never
/// looks a country up from an address.
/// </remarks>
public static class AppRegionInfo
{
    private static RegionInfo? _currentRegion;

    /// <summary>
    /// Gets or sets the app's region: the one last set, else the device's.
    /// </summary>
    public static RegionInfo CurrentRegion {
        get => _currentRegion ?? RegionInfo.CurrentRegion;
        set => _currentRegion = value;
    }

    /// <summary>
    /// Gets <see cref="CurrentRegion"/> as a country, by its two-letter code, or <see langword="null"/> for a region
    /// that is no country, such as 419, Latin America, which has no flag and no name to show.
    /// </summary>
    public static string? CurrentCountryCode =>
        CurrentRegion.Name is { Length: 2 } name && name.All(char.IsAsciiLetter) ? name : null;

    /// <summary>Makes <see cref="CurrentRegion"/> the device's again.</summary>
    internal static void Reset()
    {
        _currentRegion = null;
    }
}
