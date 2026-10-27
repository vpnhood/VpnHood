namespace VpnHood.AppLib.Api.VpnProfiles;

// A location as a state or a session reports it. A UI names the country from its code, in the
// language it shows; CountryName is the engine's English one, for a code the UI cannot name. Flat
// for the same reason as ServerLocationItem: the engine's ServerLocationInfo stays behind the contract.
public class CurrentServerLocationInfo
{
    public required string CountryCode { get; init; }
    public required string RegionName { get; init; }
    public required string ServerLocation { get; init; }
    public required string CountryName { get; init; }
    public required bool IsAuto { get; init; }
    public required bool HasRegion { get; init; }
    public bool HasMultipleRegions { get; init; }
}
