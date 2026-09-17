namespace VpnHood.AppLib.Api.ClientProfiles;

// One location a UI may pick, as the app built it: the country and region, whether it is a country
// head above its own regions, and what this person may do with it (Options). The names are carried,
// not looked up - the app fills TranslatedCountryName in the language it was told to speak.
//
// Flat on purpose. This used to derive from the engine's ServerLocationInfo, which published that
// whole type - parsing, matching, comparison and every field later added to it - to every UI and to
// the TypeScript client. What a UI reads is chosen here instead: ServerLocation, CountryName, IsAuto
// and HasRegion are the engine's answers, carried as values by whoever builds this, so the wire says
// exactly the same thing while the engine type stays behind the contract.
public class ServerLocationItem
{
    public required string CountryCode { get; init; }
    public required string RegionName { get; init; }

    // The operator's vocabulary for what this location IS ("#premium", "#unblockable", and whatever
    // else a token carries). Neither of our UIs reads it - Options below is the digested answer they
    // do read - but it is the token's own extension point, and a country head carries "~#tag" when
    // only some of its regions have one.
    public string[]? Tags { get; set; }
    public required string ServerLocation { get; init; }
    public required string CountryName { get; init; }
    public required bool IsAuto { get; init; }
    public required bool HasRegion { get; init; }
    public required bool IsNestedCountry { get; init; }
    public required bool IsDefault { get; init; }
    public string TranslatedCountryName { get; set; } = string.Empty;
    public ServerLocationOptions Options { get; set; } = new() { Normal = 0 };
}
