using System.Globalization;

namespace VpnHood.Common;

public class ServerLocationInfo : IComparable<ServerLocationInfo>
{
    public required string CountryCode { get; init; }
    public required string RegionName { get; init; }
    public string ServerLocation => $"{CountryCode}/{RegionName}";
    public string CountryName => GetCountryName(CountryCode);
    public static ServerLocationInfo Auto { get; } = new() { CountryCode = "*", RegionName = "*" };

    public int CompareTo(ServerLocationInfo other)
    {
        var countryComparison = string.Compare(CountryCode, other.CountryCode, StringComparison.OrdinalIgnoreCase);
        return countryComparison != 0 ? countryComparison : string.Compare(RegionName, other.RegionName, StringComparison.OrdinalIgnoreCase);
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
        var parts = value.Split('/');
        var ret = new ServerLocationInfo
        {
            CountryCode = ParseLocationPart(parts, 0),
            RegionName = ParseLocationPart(parts, 1)
        };
        return ret;
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
        try
        {
            if (countryCode == "*") return "(auto)";
            var regionInfo = new RegionInfo(countryCode);
            return regionInfo.EnglishName;
        }
        catch (Exception)
        {
            return countryCode;
        }
    }

    public static ServerLocationInfo[] AddCategoryGaps(string[]? selectors)
    {
        selectors ??= [];
        var locationInfos = selectors.Select(Parse).ToArray();

        var results = new List<ServerLocationInfo>();
        var countryCount = new Dictionary<string, int>();

        // Count occurrences of each country and region
        foreach (var locationInfo in locationInfos)
        {
            if (!countryCount.TryAdd(locationInfo.CountryCode, 1))
                countryCount[locationInfo.CountryCode]++;
        }

        // Add wildcard selectors for countries multiple occurrences
        var seenCountries = new HashSet<string>();
        foreach (var locationInfo in locationInfos)
        {
            var country = locationInfo.CountryCode;

            // Add wildcard selector for country if it has multiple occurrences
            if (!seenCountries.Contains(country))
            {
                if (countryCount[country] > 1)
                    results.Add(new ServerLocationInfo
                    {
                        CountryCode = locationInfo.CountryCode,
                        RegionName = "*",
                    });
                seenCountries.Add(country);
            }

            results.Add(locationInfo);
        }

        if (seenCountries.Count > 1)
            results.Insert(0, Auto);

        results.Sort();
        var distinctResults = results.Distinct().ToArray();
        return distinctResults;
    }
}