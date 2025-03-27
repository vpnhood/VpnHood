using VpnHood.Core.Common.Tokens;

namespace VpnHood.AppLib.ClientProfiles;

public class ClientServerLocationInfo : ServerLocationInfo
{
    public required bool IsNestedCountry { get; init; }
    public required bool IsDefault { get; init; }
    public ServerLocationOptions Options { get; set; } = new() { Normal = 0 };

    public static ClientServerLocationInfo[] CreateFromToken(ClientProfile clientProfile)
    {
        var clientCountry = VpnHoodApp.Instance.GetClientCountry();
        var token = clientProfile.Token;

        // get country policy
        var policy = token.ClientPolicies?.FirstOrDefault(x =>
                         x.ClientCountries.Any(y => y.Equals(clientCountry, StringComparison.OrdinalIgnoreCase))) ??
                     token.ClientPolicies?.FirstOrDefault(x => x.ClientCountries.Any(y => y == "*"));

        var items = AddCategoryGaps(token.ServerToken.ServerLocations ?? [], policy?.FreeLocations);

        // check is any items has premium tag
        var isManaged = items.Any(x => x.Tags?.Contains(ServerRegisteredTags.Premium) == true) || policy != null;
        if (isManaged) {
            foreach (var item in items)
                item.RecalculateOptions(policy, clientProfile.IsPremium); // treat non-public as premium
        }

        // show unblockable only if the policy is set
        if (policy?.UnblockableOnly == true)
            items = items.Where(x => x.Options.HasUnblockable).ToArray();

        return items;
    }


    private void RecalculateOptions(ClientPolicy? policy, bool isPremium)
    {
        var tags = Tags ?? [];
        Options = new ServerLocationOptions {
            HasFree = !tags.Contains(ServerRegisteredTags.Premium) || tags.Contains($"~{ServerRegisteredTags.Premium}"),
            HasPremium = tags.Contains(ServerRegisteredTags.Premium) ||
                         tags.Contains($"~{ServerRegisteredTags.Premium}"),
            HasUnblockable = tags.Contains(ServerRegisteredTags.Unblockable) ||
                             tags.Contains($"~{ServerRegisteredTags.Unblockable}")
        };

        if (isPremium) {
            Options.Normal = 0;
            return;
        }

        // if no policy found, set normal to 0 if there is a free location. Free location is determined by the tag #premium
        if (policy == null) {
            Options.Normal = Options.HasFree ? 0 : null;
            return;
        }

        var isBillingSupported = VpnHoodApp.Instance.Features.IsBillingSupported;
        var isRewardedAdSupported = VpnHoodApp.Instance.Services.AdService.CanShowRewarded;
        Options.Normal = Options.HasFree ? policy.Normal : null;
        Options.PremiumByTrial = Options.HasPremium ? policy.PremiumByTrial : null;
        Options.PremiumByRewardedAd = Options.HasPremium && isRewardedAdSupported ? policy.PremiumByRewardedAd : null;
        Options.PremiumByPurchase = policy.PremiumByPurchase && isBillingSupported;
        Options.PremiumByCode = policy.PremiumByCode;

        Options.Prompt = Options.PremiumByTrial != null || Options.PremiumByRewardedAd != null;
        Options.CanGoPremium = policy.PremiumByCode || policy.PremiumByPurchase; // can go premium and remove ad
    }

    private static ClientServerLocationInfo[] AddCategoryGaps(string[] serverLocations, string[]? freeLocations)
    {
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
                        Tags = [] // set it later
                    });
                }

                seenCountries.Add(countryCode);
            }

            results.Add(new ClientServerLocationInfo {
                CountryCode = item.CountryCode,
                RegionName = item.RegionName,
                IsNestedCountry = isMultipleCountry,
                IsDefault = countryCount.Count == 1 && !isMultipleCountry,
                Tags = GetItemTags(item, freeLocations).ToArray()
            });
        }

        // Add auto if there is no item or if there are multiple countries
        if (countryCount.Count > 1)
            results.Insert(0, new ClientServerLocationInfo {
                CountryCode = AutoCountryCode,
                RegionName = AutoRegionName,
                IsNestedCountry = false,
                IsDefault = true,
                Tags = [] // set it later
            });

        // set head sub auto items
        foreach (var locationInfo in results.Where(x => !x.IsAuto && x.RegionName == "*")) {
            locationInfo.Tags =
                CalcCategoryTags(results.Where(x => x.CountryCode == locationInfo.CountryCode && x.RegionName != "*"))
                    .ToArray();
        }

        // set head the auto after setting all sub auto items. This is to make sure the auto tags are calculated after all sub auto tags are set
        foreach (var locationInfo in results.Where(x => x.IsAuto)) {
            locationInfo.Tags = CalcCategoryTags(results.Where(x => x.CountryCode != AutoCountryCode)).ToArray();
        }


        results.Sort();
        var distinctResults = results.Distinct().ToArray();
        return distinctResults;
    }

    private static IEnumerable<string> CalcCategoryTags(IEnumerable<ServerLocationInfo> items)
    {
        // get distinct of all tags in items and include the partial tag (~#tag) if the tag does not present in all items
        var itemArray = items.ToArray();
        var tags = itemArray.SelectMany(x => x.Tags).Distinct().ToList();
        foreach (var tag in tags.Where(x => x.Length > 0 && x[0] != '~').ToArray()) {
            if (itemArray.Any(x => x.Tags?.Contains(tag) != true)) {
                tags.Remove(tag);
                tags.Add($"~{tag}");
            }
        }

        return tags.Distinct();
    }

    private static IEnumerable<string> GetItemTags(ServerLocationInfo item, string[]? freeLocations)
    {
        IEnumerable<string> tags = item.Tags ?? [];

        if (freeLocations == null)
            return tags;

        // check if the location is free
        var isFree = freeLocations.Contains(item.CountryCode, StringComparer.OrdinalIgnoreCase) ||
                     freeLocations.Contains("*");

        // if the location is not free, add premium tag
        if (!isFree && !tags.Contains(ServerRegisteredTags.Premium))
            tags = tags.Append(ServerRegisteredTags.Premium);

        return tags;
    }
}