using System.Globalization;
using System.Text.Json.Serialization;

namespace VpnHood.Common;

public class ServerLocationInfo : IComparable<ServerLocationInfo>
{
    public const string AutoCountryCode = "*";
    public const string AutoRegionName = "*";
    public required string CountryCode { get; init; }
    public required string RegionName { get; init; }
    public required string[] Tags { get; init; }
    public string ServerLocation => $"{CountryCode}/{RegionName}";
    public string CountryName => GetCountryName(CountryCode);

    public int CompareTo(ServerLocationInfo other)
    {
        var countryComparison = string.Compare(CountryName, other.CountryName, StringComparison.OrdinalIgnoreCase);
        return countryComparison != 0
            ? countryComparison
            : string.Compare(RegionName, other.RegionName, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj)
    {
        return ServerLocation == (obj as ServerLocationInfo)?.ServerLocation;
    }

    public override string ToString()
    {
        return ServerLocation;
    }

    public override int GetHashCode()
    {
        return ServerLocation.GetHashCode();
    }

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
        var tagsPart = (openBracketIndex < closeBracketIndex)
            ? value[(openBracketIndex + 1)..closeBracketIndex].Trim()
            : string.Empty;

        var tags = tagsPart.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Parse location parts
        var parts = locationPart.Split('/');
        var ret = new ServerLocationInfo {
            CountryCode = ParseLocationPart(parts, 0),
            RegionName = ParseLocationPart(parts, 1),
            Tags = tags
        };
        return ret;
    }


    public static ServerLocationInfo? TryParse(string value)
    {
        try {
            return Parse(value);
        }
        catch {
            return null;
        }
    }

    public bool IsMatch(ServerLocationInfo serverLocationInfo)
    {
        return
            MatchLocationPart(CountryCode, serverLocationInfo.CountryCode) &&
            MatchLocationPart(RegionName, serverLocationInfo.RegionName);
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
            return countryCode;
        }
    }

    public bool IsAuto() => IsAuto(ServerLocation);

    public static bool IsAuto(string? serverLocation)
    {
        if (string.IsNullOrEmpty(serverLocation))
            return true;

        var info = TryParse(serverLocation);
        return info is { CountryCode: AutoCountryCode, RegionName: AutoRegionName };
    }
}