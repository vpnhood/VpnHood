using VpnHood.AccessServer.Agent.Exceptions;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Tokens;
using VpnHood.Manager.Common.Utils;
using VpnHood.Server.Access.Messaging;

namespace VpnHood.AccessServer.Agent.Services;

public static class ClientPolicyCalculator
{
    public class Result
    {
        public required ServerSelectOptions ServerSelectOptions { get; init; }
        public required DateTime? ExpirationTime { get; init; }
        public required AdRequirement AdRequirement { get; init; }
        public required bool IsPremiumByToken { get; init; }
        public required bool IsPremiumByTrial { get; init; }
        public required bool IsPremiumByAdReward { get; init; }
        public required int? AdRewardMinutes { get; init; }
    }

    public static Result Calculate(
        ProjectCache projectCache,
        ServerFarmCache serverFarmCache,
        AccessTokenModel accessToken,
        AccessCache accessCache,
        SessionRequestEx sessionRequestEx,
        string? clientCountry,
        bool allowRedirect)
    {
        // find is premium and add the tags
        var clientTags = TagUtils.TagsFromString(accessToken.Tags).ToList();
        clientTags.Add(TagUtils.BuildLocation(clientCountry));
        var isPremiumToken = clientTags.Contains(TokenRegisteredTags.Premium) || !accessToken.IsPublic;
        var isPremiumByTrial = false;
        var isPremiumByAdReward = false;
        ClientPolicy? clientPolicy = null;

        // all policies applied to none premium users
        var expirationTime = accessToken.ExpirationTime;
        var adRequirement = accessToken.AdRequirement;
        string[]? allowedLocations = null;
        var serverLocationInfo = ServerLocationInfo.Parse(sessionRequestEx.ServerLocation ?? "*");

        if (!isPremiumToken) {
            var planId = sessionRequestEx.PlanId;
            var clientPolicies = accessToken.ClientPoliciesGet();
            clientPolicy =
                clientPolicies?.FirstOrDefault(x => x.ClientCountry.Equals(clientCountry, StringComparison.OrdinalIgnoreCase)) ??
                clientPolicies?.FirstOrDefault(x => x.ClientCountry == "*");

            if (clientPolicy != null) {
                // check auto-location
                if (clientPolicy.AutoLocationOnly && !serverLocationInfo.IsAuto())
                    throw new SessionExceptionEx(SessionErrorCode.AccessError, $"You need to set the location as auto. PlanId: {planId}");

                // continue AdReward if the amount is bigger than the premium by reward ad
                if (clientPolicy.PremiumByRewardAd != null && accessCache.AdRewardExpirationTime != null &&
                    accessCache.AdRewardExpirationTime >= DateTime.UtcNow.AddMinutes(clientPolicy.PremiumByRewardAd.Value)) {
                    expirationTime = accessCache.AdRewardExpirationTime;
                    adRequirement = AdRequirement.None;
                    isPremiumByAdReward = true;
                }

                // normal plan
                else if (planId == ConnectPlanId.Normal) {
                    if (clientPolicy.Normal is null or < 0)
                        throw new SessionExceptionEx(SessionErrorCode.AccessError, $"The connect plan is not supported. PlanId: {planId}");

                    expirationTime = clientPolicy.Normal == 0 ? null : DateTime.UtcNow.AddMinutes(clientPolicy.Normal.Value);
                    adRequirement = accessToken.AdRequirement;
                    allowedLocations = clientPolicy.FreeLocations;
                }

                // trial plan
                else if (planId == ConnectPlanId.PremiumByTrial) {
                    if (clientPolicy.PremiumByTrial is null or < 0)
                        throw new SessionExceptionEx(SessionErrorCode.AccessError, $"The connect plan is not supported. PlanId: {planId}");

                    expirationTime = clientPolicy.PremiumByTrial == 0 ? null : DateTime.UtcNow.AddMinutes(clientPolicy.PremiumByTrial.Value);
                    adRequirement = AdRequirement.None;
                    isPremiumByTrial = true;
                }

                // Rewarded Ad plan
                else if (planId == ConnectPlanId.PremiumByAdReward) {
                    if (clientPolicy.PremiumByRewardAd is null or < 0)
                        throw new SessionExceptionEx(SessionErrorCode.AccessError, $"The connect plan is not supported. PlanId: {planId}");

                    expirationTime = clientPolicy.PremiumByRewardAd.Value == 0 ? null : DateTime.UtcNow.AddMinutes(clientPolicy.PremiumByRewardAd.Value);
                    adRequirement = AdRequirement.Required;
                    isPremiumByAdReward = true;

                    // continue previous AdReward if the remain time is bigger than 30% of the premium by reward ad
                    var remainTime = accessCache.AdRewardExpirationTime - DateTime.UtcNow;
                    if (remainTime?.TotalMinutes > clientPolicy.PremiumByRewardAd.Value * 0.3) {
                        expirationTime = accessCache.AdRewardExpirationTime;
                        adRequirement = AdRequirement.None;
                        isPremiumByAdReward = true;
                    }
                }
            }
        }

        // expiration time can not bigger that the token expiration time
        if (expirationTime > accessToken.ExpirationTime)
            expirationTime = accessToken.ExpirationTime;

        // fill result
        var serverSelectOptions = new ServerSelectOptions {
            ProjectCache = projectCache,
            ServerFarmCache = serverFarmCache,
            ClientTags = clientTags.ToArray(),
            IncludeIpV6 = sessionRequestEx.IsIpV6Supported == true || sessionRequestEx.HostEndPoint.IsV6(),
            RequestedLocation = ServerLocationInfo.Parse(sessionRequestEx.ServerLocation ?? "*"),
            AllowedLocations = allowedLocations,
            AllowRedirect = sessionRequestEx.AllowRedirect && allowRedirect,
            IsPremium = isPremiumToken || isPremiumByAdReward || isPremiumByTrial
        };

        var result = new Result {
            ServerSelectOptions = serverSelectOptions,
            ExpirationTime = expirationTime,
            AdRequirement = adRequirement,
            IsPremiumByToken = isPremiumToken,
            IsPremiumByTrial = isPremiumByTrial,
            IsPremiumByAdReward = isPremiumByAdReward,
            AdRewardMinutes = clientPolicy?.PremiumByRewardAd
        };

        return result;
    }
}