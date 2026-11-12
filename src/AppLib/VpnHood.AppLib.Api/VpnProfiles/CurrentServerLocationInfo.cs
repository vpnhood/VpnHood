namespace VpnHood.AppLib.Api.VpnProfiles;

/// <summary>
/// Represents a server location as the state or a session reports it; the UI names the country from its code, in the
/// language it shows.
/// </summary>
public class CurrentServerLocationInfo
{
    /// <summary>Gets the country's two-letter code, or <c>*</c> for the automatic location.</summary>
    public required string CountryCode { get; init; }

    /// <summary>Gets the region's name, or <c>*</c> for any region.</summary>
    public required string RegionName { get; init; }

    /// <summary>Gets the location without its tags, as <c>US/California</c>.</summary>
    public required string ServerLocation { get; init; }

    /// <summary>Gets the country's English name, for a code the UI cannot name.</summary>
    public required string CountryName { get; init; }

    /// <summary>Gets a value indicating whether this is the automatic location, <c>*/*</c>.</summary>
    public required bool IsAuto { get; init; }

    /// <summary>Gets a value indicating whether the location names a region.</summary>
    public required bool HasRegion { get; init; }

    /// <summary>Gets a value indicating whether the VPN profile's locations have more than one region in the country.</summary>
    public bool HasMultipleRegions { get; init; }
}
