using System.Globalization;

namespace VpnHood.Core.Common.Tokens;

/// <summary>
/// Represents a server location: a country, a region in it, and tags, written as <c>US/California [#premium]</c>.
/// </summary>
/// <remarks>Two locations are equal when their countries and regions are, whatever their tags.</remarks>
public class ServerLocationInfo : IComparable<ServerLocationInfo>
{
    /// <summary>The country code of the automatic location: <c>*</c>.</summary>
    public const string AutoCountryCode = "*";

    /// <summary>The region name of the automatic location, or of any region: <c>*</c>.</summary>
    public const string AutoRegionName = "*";

    /// <summary>Gets the country's two-letter code, or <see cref="AutoCountryCode"/>.</summary>
    public required string CountryCode { get; init; }

    /// <summary>Gets the region's name, or <see cref="AutoRegionName"/>.</summary>
    public required string RegionName { get; init; }

    /// <summary>Gets or sets the location's tags, such as those of <see cref="ServerRegisteredTags"/>, or <see langword="null"/>.</summary>
    public string[]? Tags { get; set; }

    /// <summary>Gets the location without its tags, as <c>US/California</c>.</summary>
    public string ServerLocation => $"{CountryCode}/{RegionName}";

    /// <summary>
    /// Gets the country's English name; <c>*</c> for the automatic location, and the code in upper case where .NET
    /// knows no country of it.
    /// </summary>
    public string CountryName => CountryCode== AutoCountryCode 
    ? AutoCountryCode
    : GetCountryName(CountryCode);

    /// <summary>Gets a value indicating whether this is the automatic location, <c>*/*</c>.</summary>
    public bool IsAuto => IsAutoLocation(ServerLocation);

    /// <summary>Gets a value indicating whether the location names a region.</summary>
    public bool HasRegion => !string.IsNullOrEmpty(RegionName) && RegionName != AutoRegionName;

    /// <summary>Compares the location with another, by country name, then region name, ignoring case.</summary>
    /// <param name="other">The other location, or <see langword="null"/>, which sorts first.</param>
    /// <returns>Less than 0 if this location sorts first, 0 if they sort together, more than 0 if it sorts after.</returns>
    public int CompareTo(ServerLocationInfo? other)
    {
        if (other == null) return 1; // null is considered less than any instance

        var countryComparison = string.Compare(CountryName, other.CountryName, StringComparison.OrdinalIgnoreCase);
        return countryComparison != 0
            ? countryComparison
            : string.Compare(RegionName, other.RegionName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Determines whether an object is a location of the same country and region.</summary>
    /// <param name="obj">The object.</param>
    /// <returns><see langword="true"/> if it is; otherwise, <see langword="false"/>.</returns>
    /// <remarks>The codes and names are compared as written, case included.</remarks>
    public override bool Equals(object? obj)
    {
        return ServerLocation == (obj as ServerLocationInfo)?.ServerLocation;
    }

    /// <summary>Gets a hash of the location's country and region.</summary>
    /// <returns>The hash.</returns>
    public override int GetHashCode()
    {
        return ServerLocation.GetHashCode();
    }

    /// <summary>Writes the location as <see cref="Parse"/> reads it, with its tags in brackets when it has any.</summary>
    /// <returns>The text, such as <c>US/California [#premium]</c>.</returns>
    public override string ToString()
    {
        return Tags?.Length > 0
            ? $"{ServerLocation} [{string.Join(" ", Tags)}]"
            : ServerLocation;
    }

    /// <summary>Reads a location written as <c>CountryCode/RegionName [#tag1 #tag2]</c>.</summary>
    /// <param name="value">
    /// The text. A missing or blank country or region reads as <c>*</c>; the country code is made upper case; the
    /// tags, in brackets, are split at spaces.
    /// </param>
    /// <returns>The location.</returns>
    public static ServerLocationInfo Parse(string value)
    {
        // format is "CountryCode/RegionName [#tag1 #tag2 ...]"

        // Parse location and tags
        var openBracketIndex = value.IndexOf('[');
        var closeBracketIndex = value.LastIndexOf(']');

        // If no brackets found, set indices appropriately
        if (openBracketIndex == -1 || closeBracketIndex == -1 || closeBracketIndex < openBracketIndex) {
            openBracketIndex = value.Length;
            closeBracketIndex = value.Length; // No tags present
        }

        var locationPart = value[..openBracketIndex].Trim();

        // Extract tags only if both brackets are found and in the correct order
        var tagsPart = openBracketIndex < closeBracketIndex
            ? value[(openBracketIndex + 1)..closeBracketIndex].Trim()
            : string.Empty;

        var tags = tagsPart.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Parse location parts
        var parts = locationPart.Split('/');
        var ret = new ServerLocationInfo {
            CountryCode = ParseLocationPart(parts, 0).ToUpper(),
            RegionName = ParseLocationPart(parts, 1),
            Tags = tags
        };
        return ret;
    }


    /// <summary>Reads a location as <see cref="Parse"/> does, or gives <see langword="null"/> where that throws.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The location, or <see langword="null"/>.</returns>
    public static ServerLocationInfo? TryParse(string value)
    {
        try {
            return Parse(value);
        }
        catch {
            return null;
        }
    }

    /// <summary>
    /// Determines whether this location, a server's, matches a location asked for, whose <c>*</c> parts match any.
    /// </summary>
    /// <param name="serverLocationInfo">The location asked for.</param>
    /// <returns><see langword="true"/> if it matches, ignoring case; otherwise, <see langword="false"/>.</returns>
    public bool IsMatch(ServerLocationInfo serverLocationInfo)
    {
        return
            MatchLocationPart(CountryCode, serverLocationInfo.CountryCode) &&
            MatchLocationPart(RegionName, serverLocationInfo.RegionName);
    }

    /// <summary>Determines whether a written location is this one, as <see cref="Equals(object?)"/> compares them.</summary>
    /// <param name="serverLocation">The written location, or <see langword="null"/>, which equals the automatic one.</param>
    /// <returns><see langword="true"/> if it is; otherwise, <see langword="false"/>.</returns>
    public bool LocationEquals(string? serverLocation)
    {
        if (serverLocation == null) return IsAuto;
        var other = TryParse(serverLocation);
        return Equals(other);
    }


    private static string ParseLocationPart(IReadOnlyList<string> parts, int index)
    {
        return parts.Count <= index || string.IsNullOrWhiteSpace(parts[index]) ? "*" : parts[index].Trim();
    }

    private static bool MatchLocationPart(string serverPart, string requestPart)
    {
        return requestPart == "*" || requestPart.Equals(serverPart, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCountryName(string countryCode)
    {
        try {
            if (countryCode == "*") return "(auto)";
            var regionInfo = new RegionInfo(countryCode);
            return regionInfo.EnglishName;
        }
        catch (Exception) {
            return countryCode.ToUpper();
        }
    }

    /// <summary>Determines whether a written location is the automatic one.</summary>
    /// <param name="serverLocation">The written location.</param>
    /// <returns>
    /// <see langword="true"/> if it is null, empty, or reads as <c>*/*</c>; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool IsAutoLocation(string? serverLocation)
    {
        if (string.IsNullOrEmpty(serverLocation))
            return true;

        var info = TryParse(serverLocation);
        return info is { CountryCode: AutoCountryCode, RegionName: AutoRegionName };
    }
}