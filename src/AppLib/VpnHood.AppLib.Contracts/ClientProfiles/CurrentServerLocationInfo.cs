namespace VpnHood.AppLib.Contracts.ClientProfiles;

// A location as a state or a session reports it. The app fills the translated name; the shape
// carries it, which is what keeps the country database out of the contract. Flat for the same
// reason as ServerLocationItem: the engine's ServerLocationInfo stays behind the contract.
public class CurrentServerLocationInfo
{
    public required string CountryCode { get; init; }
    public required string RegionName { get; init; }
    public required string ServerLocation { get; init; }
    public required string CountryName { get; init; }
    public required bool IsAuto { get; init; }
    public required bool HasRegion { get; init; }
    public bool HasMultipleRegions { get; init; }
    public string TranslatedCountryName { get; set; } = string.Empty;
}
