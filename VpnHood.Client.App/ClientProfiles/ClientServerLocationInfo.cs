using VpnHood.Common;

namespace VpnHood.Client.App.ClientProfiles;

public class ClientServerLocationInfo : ServerLocationInfo
{
    public required bool IsNestedCountry { get; init; }
    public required bool IsSameAsGlobalAuto { get; init; }

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
            var countryCode = locationInfo.CountryCode;

            // Add wildcard selector for country if it has multiple occurrences
            var isMultipleCountry = countryCount[countryCode] > 1;
            if (!seenCountries.Contains(countryCode))
            {
                if (isMultipleCountry)
                    results.Add(new ClientServerLocationInfo
                    {
                        CountryCode = locationInfo.CountryCode,
                        RegionName = "*",
                        IsNestedCountry = false,
                        IsSameAsGlobalAuto = countryCount.Count == 1
                    });
                seenCountries.Add(countryCode);
            }

            results.Add(new ClientServerLocationInfo
            {
                CountryCode = locationInfo.CountryCode,
                RegionName = locationInfo.RegionName,
                IsNestedCountry = isMultipleCountry,
                IsSameAsGlobalAuto = countryCount.Count == 1 && !isMultipleCountry
            });
        }

        // Add auto if there is no item or if there are multiple countries
        if (countryCount.Count > 1)
            results.Insert(0, new ClientServerLocationInfo
            {
                CountryCode = Auto.CountryCode,
                RegionName = Auto.RegionName,
                IsNestedCountry = false,
                IsSameAsGlobalAuto = true
            });

        results.Sort();
        var distinctResults = results.Distinct().ToArray();
        return distinctResults;
    }
}