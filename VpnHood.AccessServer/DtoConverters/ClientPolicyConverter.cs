using VpnHood.AccessServer.Dtos.ClientPolicies;

namespace VpnHood.AccessServer.DtoConverters;

public static class ClientPolicyConverter
{
    public static ClientPolicy ToDto(this VpnHood.Common.Tokens.ClientPolicy tokenClientPolicy)
    {
        var dto = new ClientPolicy {
            CountryCode = tokenClientPolicy.CountryCode,
            FreeLocations = tokenClientPolicy.FreeLocations,
            AutoLocationOnly = tokenClientPolicy.AutoLocationOnly,
            Normal = tokenClientPolicy.Normal,
            PremiumByTrial = tokenClientPolicy.PremiumByTrial,
            PremiumByRewardAd = tokenClientPolicy.PremiumByRewardAd,
            PremiumByPurchase = tokenClientPolicy.PremiumByPurchase,
            PremiumByCode = tokenClientPolicy.PremiumByCode
        };
        return dto;
    }

    public static VpnHood.Common.Tokens.ClientPolicy ToTokenPolicy(this ClientPolicy clientPolicy)
    {
        var tokenClientPolicy = new VpnHood.Common.Tokens.ClientPolicy {
            CountryCode = clientPolicy.CountryCode,
            FreeLocations = clientPolicy.FreeLocations,
            AutoLocationOnly = clientPolicy.AutoLocationOnly,
            Normal = clientPolicy.Normal,
            PremiumByTrial = clientPolicy.PremiumByTrial,
            PremiumByRewardAd = clientPolicy.PremiumByRewardAd,
            PremiumByPurchase = clientPolicy.PremiumByPurchase,
            PremiumByCode = clientPolicy.PremiumByCode
        };
        return tokenClientPolicy;
    }

    public static ClientPolicy[]? ToDtos(this VpnHood.Common.Tokens.ClientPolicy[]? policies)
    {
        return policies?.Select(x => x.ToDto()).ToArray();
    }

    public static VpnHood.Common.Tokens.ClientPolicy[]? ToTokenPolicies(this ClientPolicy[]? clientPolicies)
    {
        return clientPolicies?.Select(x => x.ToTokenPolicy()).ToArray();
    }

}