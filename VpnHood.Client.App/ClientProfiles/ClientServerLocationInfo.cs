using VpnHood.Common;

namespace VpnHood.Client.App.ClientProfiles;

public class ClientServerLocationInfo : ServerLocationInfo
{
    public required bool IsNestedCountry { get; init; }
    public required bool IsDefault { get; init; }

    public static ClientServerLocationInfo[] AddCategoryGaps(string[]? serverLocations)
    {
        serverLocations ??= [];
        var items = serverLocations.Select(Parse).ToArray();

        var results = new List<ClientServerLocationInfo>();
        var countryCount = new Dictionary<string, int>();

        // Count occurrences of each country and region
        foreach (var item in items) {
            if (!countryCount.TryAdd(item.CountryCode, 1))
                countryCount[item.CountryCode]++;
        }

        // Add wildcard serverLocations for countries multiple occurrences
        var seenCountries = new HashSet<string>();
        foreach (var item in items) {
            var countryCode = item.CountryCode;

            // Add wildcard selector for country if it has multiple occurrences
            var isMultipleCountry = countryCount[countryCode] > 1;
            if (!seenCountries.Contains(countryCode)) {
                if (isMultipleCountry) {
                    results.Add(new ClientServerLocationInfo {
                        CountryCode = countryCode,
                        RegionName = "*",
                        IsNestedCountry = false,
                        IsDefault = countryCount.Count == 1,
                        Tags = CalcCategoryTags(items.Where(x => x.CountryCode == countryCode)).ToArray()
                    });
                }

                seenCountries.Add(countryCode);
            }

            results.Add(new ClientServerLocationInfo {
                CountryCode = item.CountryCode,
                RegionName = item.RegionName,
                IsNestedCountry = isMultipleCountry,
                IsDefault = countryCount.Count == 1 && !isMultipleCountry,
                Tags = item.Tags
            });
        }

        // Add auto if there is no item or if there are multiple countries
        if (countryCount.Count > 1)
            results.Insert(0, new ClientServerLocationInfo {
                CountryCode = AutoCountryCode,
                RegionName = AutoRegionName,
                IsNestedCountry = false,
                IsDefault = true,
                Tags = CalcCategoryTags(items).ToArray()
            });

        results.Sort();
        var distinctResults = results.Distinct().ToArray();
        return distinctResults;
    }

    private static IEnumerable<string> CalcCategoryTags(IEnumerable<ServerLocationInfo> items)
    {
        // get distinct of all tags in items and include the not tag (!#tag) if the tag is not present is all items
        var itemArray = items.ToArray();
        var tags = itemArray.SelectMany(x => x.Tags).Distinct().ToList();
        foreach (var tag in tags.ToArray()) {
            if (itemArray.Any(x=>!x.Tags.Contains(tag)))
                tags.Add($"!{tag}");
        }

        return tags.Distinct();
    }
}