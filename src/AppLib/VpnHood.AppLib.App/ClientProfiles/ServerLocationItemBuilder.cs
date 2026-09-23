using System.Diagnostics.CodeAnalysis;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.ClientProfiles;
using VpnHood.AppLib.App.Services.Countries;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.AppLib.App.ClientProfiles;

// The locations a profile offers, built from its token: the category gaps a UI needs (a country
// head above its regions, the automatic one above the countries), then what each location costs
// this person under the operator's policy. It lives here and not beside the shape it returns
// because it reads the token, and a token is the app's, never the contract's.
//
// Two answers come out of one input. "#premium", "~#premium", "#unblockable" are the operator's
// vocabulary for what a location IS; Options is what this person may DO with it. Both reach a UI on
// the picker row, but only Options reaches the current-location report - nothing reads a tag there.
// The drafts below carry the tags while the answer is being worked out, which is why the row is
// built once, at the end.
internal static class ServerLocationItemBuilder
{
    public static ServerLocationItem[] Build(ClientProfile clientProfile, AppFeatures appFeatures)
    {
        var clientCountry = AppRegionInfo.CurrentRegion.Name;
        var token = clientProfile.Token;

        // get country policy
        var policy = token.ClientPolicies?.FirstOrDefault(x =>
                         x.ClientCountries.Any(y => y.Equals(clientCountry, StringComparison.OrdinalIgnoreCase))) ??
                     token.ClientPolicies?.FirstOrDefault(x => x.ClientCountries.Any(y => y == "*"));

        var drafts = AddCategoryGaps(token.ServerToken.ServerLocations ?? [], policy?.FreeLocations);

        // check is any items has premium tag
        var isManaged = drafts.Any(x => x.Tags.Contains(ServerRegisteredTags.Premium)) || policy != null;
        if (isManaged) {
            foreach (var draft in drafts)
                RecalculateOptions(draft, policy, clientProfile.IsPremium, appFeatures); // treat non-public as premium
        }

        // show unblockable only if the policy is set
        if (policy?.UnblockableOnly == true)
            drafts = [.. drafts.Where(x => x.Options.HasUnblockable)];

        // the name a UI shows, in the language the app was told to speak: the shape carries the
        // name, it does not look one up, which is what keeps the country database out of the
        // contract and out of a paired browser's download
        return [.. drafts.Select(ToItem)];
    }

    private static ServerLocationItem ToItem(LocationDraft draft)
    {
        var location = draft.Location;
        return new ServerLocationItem {
            CountryCode = location.CountryCode,
            RegionName = location.RegionName,
            Tags = draft.Tags,
            ServerLocation = location.ServerLocation,
            CountryName = location.CountryName,
            IsAuto = location.IsAuto,
            HasRegion = location.HasRegion,
            IsNestedCountry = draft.IsNestedCountry,
            IsDefault = draft.IsDefault,
            Options = draft.Options,
            TranslatedCountryName = location.CountryCode == ServerLocationInfo.AutoCountryCode
                ? ServerLocationInfo.AutoCountryCode
                : AppCountryInfo.TryGet(location.CountryCode)?.TranslatedName ?? location.CountryName
        };
    }

    private static void RecalculateOptions(LocationDraft draft, Core.Common.Tokens.ClientPolicy? policy,
        bool isPremium, AppFeatures appFeatures)
    {
        var tags = draft.Tags;
        var options = new ServerLocationOptions {
            HasFree = !tags.Contains(ServerRegisteredTags.Premium) ||
                      tags.Contains($"~{ServerRegisteredTags.Premium}"),
            HasPremium = tags.Contains(ServerRegisteredTags.Premium) ||
                         tags.Contains($"~{ServerRegisteredTags.Premium}"),
            HasUnblockable = tags.Contains(ServerRegisteredTags.Unblockable) ||
                             tags.Contains($"~{ServerRegisteredTags.Unblockable}")
        };
        draft.Options = options;

        if (isPremium) {
            options.Normal = 0;
            return;
        }

        // if no policy found, set normal to 0 if there is a free location. Free location is determined by the tag #premium
        if (policy == null) {
            options.Normal = options.HasFree ? 0 : null;
            return;
        }

        var isRewardedAdSupported = appFeatures.IsRewardedAdSupported;
        options.Normal = options.HasFree ? policy.Normal : null;
        options.NormalByRewardedAd = options.HasFree && isRewardedAdSupported ? policy.NormalByRewardedAd : null;

        // A GIVEN premium session — a better location for a while — survives a build with no premium
        // tier: it costs the person nothing, passes through no store, and is the server's to hand
        // out, exactly like NormalByRewardedAd above. What a tier is needed for is SELLING one.
        options.PremiumByTrial = options.HasPremium ? policy.PremiumByTrial : null;
        options.PremiumByRewardedAd = options.HasPremium && isRewardedAdSupported ? policy.PremiumByRewardedAd : null;

        // The two SOLD routes. Each is offered only where this BUILD can finish it: a purchase needs
        // a store or an outside shop the build may open, a code needs a build allowed to take one,
        // and both need a build that sells premium at all. The policy speaks for the operator that
        // issued the token and knows nothing about how this build was shipped, so what it offers is
        // intersected with what the build permits — otherwise a head advertises a route that ends on
        // an empty purchase page, or none at all.
        var premium = appFeatures.Premium;
        var isPurchasableHere = premium != null && (appFeatures.IsBillingSupported ||
                                                    (premium.IsPurchaseUrlSupported && policy.PurchaseUrl != null));
        options.PremiumByPurchase = policy.PremiumByPurchase && isPurchasableHere;
        options.PremiumByCode = policy.PremiumByCode && premium?.AllowImportAccessCode == true;

        options.Prompt = options.PremiumByTrial != null || options.PremiumByRewardedAd != null ||
                         options.NormalByRewardedAd != null;
        // can go premium and remove ad
        options.CanGoPremium = options.PremiumByCode || options.PremiumByPurchase;
    }

    private static LocationDraft[] AddCategoryGaps(string[] serverLocations, string[]? freeLocations)
    {
        var items = serverLocations.Select(ServerLocationInfo.Parse).ToArray();
        var results = new List<LocationDraft>();
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
                    results.Add(new LocationDraft {
                        Location = new ServerLocationInfo { CountryCode = countryCode, RegionName = "*" },
                        IsNestedCountry = false,
                        IsDefault = countryCount.Count == 1
                    }); // tags set later
                }

                seenCountries.Add(countryCode);
            }

            results.Add(new LocationDraft {
                Location = item,
                IsNestedCountry = isMultipleCountry,
                IsDefault = countryCount.Count == 1 && !isMultipleCountry,
                Tags = [.. GetItemTags(item, freeLocations)]
            });
        }

        // Add auto if there is no item or if there are multiple countries
        if (countryCount.Count > 1)
            results.Insert(0, new LocationDraft {
                Location = new ServerLocationInfo {
                    CountryCode = ServerLocationInfo.AutoCountryCode,
                    RegionName = ServerLocationInfo.AutoRegionName
                },
                IsNestedCountry = false,
                IsDefault = true
            }); // tags set later

        // set head sub auto items
        foreach (var draft in results.Where(x => x is { Location.IsAuto: false, Location.RegionName: "*" })) {
            draft.Tags = [
                .. CalcCategoryTags(results.Where(x =>
                    x.Location.CountryCode == draft.Location.CountryCode && x.Location.RegionName != "*"))
            ];
        }

        // set head the auto after setting all sub auto items. This is to make sure the auto tags are calculated after all sub auto tags are set
        foreach (var draft in results.Where(x => x.Location.IsAuto)) {
            draft.Tags = [
                .. CalcCategoryTags(results.Where(x => x.Location.CountryCode != ServerLocationInfo.AutoCountryCode))
            ];
        }

        // What ServerLocationInfo's IComparable and Equals did, said out loud now that the contract's
        // shape is flat: ordered by country name then region, and one entry per location. List.Sort is
        // kept (not OrderBy) so equal keys keep the same order they had before.
        results.Sort((a, b) => {
            var countryComparison = string.Compare(a.Location.CountryName, b.Location.CountryName,
                StringComparison.OrdinalIgnoreCase);
            return countryComparison != 0
                ? countryComparison
                : string.Compare(a.Location.RegionName, b.Location.RegionName, StringComparison.OrdinalIgnoreCase);
        });

        return [.. results.DistinctBy(x => x.Location.ServerLocation)];
    }

    private static IEnumerable<string> CalcCategoryTags(IEnumerable<LocationDraft> items)
    {
        // get distinct of all tags in items and include the partial tag (~#tag) if the tag does not present in all items
        var itemArray = items.ToArray();
        var tags = itemArray.SelectMany(x => x.Tags).Distinct().ToList();
        foreach (var tag in tags.Where(x => x.Length > 0 && x[0] != '~').ToArray()) {
            if (itemArray.Any(x => !x.Tags.Contains(tag))) {
                tags.Remove(tag);
                tags.Add($"~{tag}");
            }
        }

        return tags.Distinct();
    }

    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    private static IEnumerable<string> GetItemTags(ServerLocationInfo item, string[]? freeLocations)
    {
        IEnumerable<string> tags = item.Tags ?? [];

        if (freeLocations == null)
            return tags;

        // check if the location is free
        var isFree = freeLocations.Contains(item.CountryCode, StringComparer.OrdinalIgnoreCase) ||
                     freeLocations.Contains("*");

        // if the location is not free, add premium tag
        if (!isFree) {
            // remove partial premium tag if it exists
            tags = tags.Where(x => x != $"~{ServerRegisteredTags.Premium}");

            // add premium tag if it does not exist
            if (!tags.Contains(ServerRegisteredTags.Premium))
                tags = tags.Append(ServerRegisteredTags.Premium);
        }

        return tags;
    }

    // One location while it is being worked out: the engine's parsed shape, the operator's tags, and
    // the answers being computed from them. Never leaves this class.
    private sealed class LocationDraft
    {
        public required ServerLocationInfo Location { get; init; }
        public required bool IsNestedCountry { get; init; }
        public required bool IsDefault { get; init; }
        public string[] Tags { get; set; } = [];
        public ServerLocationOptions Options { get; set; } = new() { Normal = 0 };
    }
}
