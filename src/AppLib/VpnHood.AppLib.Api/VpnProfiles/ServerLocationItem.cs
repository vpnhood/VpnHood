namespace VpnHood.AppLib.Api.VpnProfiles;

//
// Flat on purpose. This used to derive from the engine's ServerLocationInfo, which published that
// whole type - parsing, matching, comparison and every field later added to it - to every UI and to
// the TypeScript client. What a UI reads is chosen here instead: ServerLocation, CountryName, IsAuto
// and HasRegion are the engine's answers, carried as values by whoever builds this, so the wire says
// exactly the same thing while the engine type stays behind the contract.

/// <summary>
/// Represents a server location the person may pick, as the app built it: the country and region, whether it is a
/// country above its own regions, and what the person may do with it.
/// </summary>
/// <remarks>The UI names the country from its code, in the language it shows.</remarks>
public class ServerLocationItem
{
    /// <summary>Gets the country's two-letter code, or <c>*</c> for the automatic location.</summary>
    public required string CountryCode { get; init; }

    /// <summary>Gets the region's name, or <c>*</c> for any region.</summary>
    public required string RegionName { get; init; }

    /// <summary>
    /// Gets or sets the operator's tags of the location, as <c>#premium</c> and <c>#unblockable</c>, or
    /// <see langword="null"/>; a country above its regions carries <c>~#tag</c> where only some of them have one.
    /// </summary>
    /// <remarks><see cref="Options"/> is what they mean for the person.</remarks>
    public IReadOnlyList<string>? Tags { get; set; }

    /// <summary>Gets the location without its tags, as <c>US/California</c>.</summary>
    public required string ServerLocation { get; init; }

    /// <summary>Gets the country's English name, for a code the UI cannot name.</summary>
    public required string CountryName { get; init; }

    /// <summary>Gets a value indicating whether this is the automatic location, <c>*/*</c>.</summary>
    public required bool IsAuto { get; init; }

    /// <summary>Gets a value indicating whether the location names a region.</summary>
    public required bool HasRegion { get; init; }

    /// <summary>Gets a value indicating whether the location is listed under its country's own entry, as one of its several regions.</summary>
    public required bool IsNestedCountry { get; init; }

    /// <summary>Gets a value indicating whether the location is the one picked when the person picks none.</summary>
    public required bool IsDefault { get; init; }

    /// <summary>Gets or sets what the person may do with the location. The default offers an ordinary connection with no limit.</summary>
    public ServerLocationOptions Options { get; set; } = new() { Normal = 0 };
}
