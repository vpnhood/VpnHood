using VpnHood.Common;

namespace VpnHood.Client.App.ClientProfiles;

public class ClientServerLocationInfo : ServerLocationInfo
{
    public required bool IsNestedCountry { get; init; }

    private static ClientServerLocationInfo FromBase(ServerLocationInfo locationInfo, bool isNestedCountry)
    {
        return new ClientServerLocationInfo
        {
            CountryCode = locationInfo.CountryCode,
            RegionName = locationInfo.RegionName,
            IsNestedCountry = isNestedCountry
        };
    }

    public static ClientServerLocationInfo[] AddCategoryGaps(string[]? serverLocations)
    {
        serverLocations ??= [];
        var locationInfos = serverLocations.Select(Parse).ToArray();

        var results = new List<ClientServerLocationInfo>();
        var countryCount = new Dictionary<string, int>();

        // Count occurrences of each country and region
        foreach (var locationInfo in locationInfos)
        {
            if (!countryCount.TryAdd(locationInfo.CountryCode, 1))
                countryCount[locationInfo.CountryCode]++;
        }

        // Add wildcard serverLocations for countries multiple occurrences
        var seenCountries = new HashSet<string>();
        foreach (var locationInfo in locationInfos)
        {
            var country = locationInfo.CountryCode;

            // Add wildcard selector for country if it has multiple occurrences
            var isMultipleCountry = countryCount[country] > 1;
            if (!seenCountries.Contains(country))
            {
                if (isMultipleCountry)
                    results.Add(new ClientServerLocationInfo
                    {
                        CountryCode = locationInfo.CountryCode,
                        RegionName = "*",
                        IsNestedCountry = false
                    });
                seenCountries.Add(country);
            }

            results.Add(FromBase(locationInfo, isMultipleCountry));
        }

        // Add auto if there is no item or if there are multiple countries
        if (seenCountries.Count > 1)
            results.Insert(0, FromBase(Auto, false));

        results.Sort();
        var distinctResults = results.Distinct().ToArray();
        return distinctResults;
    }
}