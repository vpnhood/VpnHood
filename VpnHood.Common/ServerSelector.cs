using System.Globalization;

namespace VpnHood.Common;

public class ServerSelector
{
    public required string CountryCode { get; init; }
    public required string RegionName { get; init; }
    public required string CityName { get; init; }
    public string ServerSelectorId => $"{CountryCode}/{RegionName}/{CityName}";
    public string CountryName => GetCountryName(CountryCode);

    public override string ToString()
    {
        return ServerSelectorId;
    }

    public static ServerSelector Parse(string value)
    {
        var parts = value.Split('/');

        var ret = new ServerSelector
        {
            CountryCode = ParseLocationPart(parts, 0),
            RegionName = ParseLocationPart(parts, 1),
            CityName = ParseLocationPart(parts, 2),
        };
        return ret;
    }

    public bool IsMatched(ServerSelector serverSelector)
    {
        return
            MatchLocationPart(CountryCode, serverSelector.CountryCode) &&
            MatchLocationPart(RegionName, serverSelector.RegionName) &&
            MatchLocationPart(CityName, serverSelector.CityName);
    }

    private static string ParseLocationPart(IReadOnlyList<string> parts, int index)
    {
        return parts.Count <= index || string.IsNullOrWhiteSpace(parts[index]) ? "-" : parts[index].Trim();
    }

    private static bool MatchLocationPart(string serverPart, string requestPart)
    {
        return requestPart == "*" || requestPart.Equals(serverPart, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCountryName(string regionCode)
    {
        try
        {
            var regionInfo = new RegionInfo(regionCode);
            return regionInfo.EnglishName;
        }
        catch (Exception)
        {
            return regionCode;
        }
    }

    public static ServerSelector[] AddCategoryGaps(string[]? selectors)
    {
        selectors ??= [];
        var serverSelectors = selectors.Select(Parse).ToArray();

        var result = new List<ServerSelector>();
        var countryCount = new Dictionary<string, int>();
        var regionCount = new Dictionary<string, int>();

        // Count occurrences of each country and region
        foreach (var serverSelector in serverSelectors)
        {
            if (!countryCount.TryAdd(serverSelector.CountryCode, 1))
                countryCount[serverSelector.CountryCode]++;

            var regionKey = $"{serverSelector.CountryCode}/{serverSelector.RegionName}";
            if (!regionCount.TryAdd(regionKey, 1))
                regionCount[regionKey]++;
        }

        // Add wildcard selectors for countries and regions with multiple occurrences
        var seenCountries = new HashSet<string>();
        var seenRegions = new HashSet<string>();
        foreach (var serverSelector in serverSelectors)
        {
            var country = serverSelector.CountryCode;
            var region = $"{serverSelector.CountryCode}/{serverSelector.RegionName}";

            // Add wildcard selector for country if it has multiple occurrences
            if (!seenCountries.Contains(country))
            {
                if (countryCount[country] > 1)
                    result.Add(new ServerSelector
                    {
                        CountryCode = serverSelector.CountryCode,
                        RegionName = "*",
                        CityName = "*"
                    });
                seenCountries.Add(country);
            }

            // Add wildcard selector for region if it has multiple occurrences
            if (!seenRegions.Contains(region))
            {
                if (regionCount[region] > 1)
                    result.Add(new ServerSelector
                    {
                        CountryCode = serverSelector.CountryCode,
                        RegionName = serverSelector.RegionName,
                        CityName = "*"
                    });

                seenRegions.Add(region);
            }

            result.Add(serverSelector);
        }

        return result.ToArray();
    }
}