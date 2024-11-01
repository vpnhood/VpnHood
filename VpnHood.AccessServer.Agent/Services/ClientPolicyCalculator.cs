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
        public required bool IsPremium { get; init; }
    }

    public static Result Calculate(
        ProjectCache projectCache, 
        ServerFarmCache serverFarmCache,
        AccessTokenModel accessToken,
        SessionRequestEx sessionRequestEx,
        string? clientCountry,
        bool allowRedirect)
    {
        // find is premium and add the tags
        var clientTags = TagUtils.TagsFromString(accessToken.Tags).ToList();
        clientTags.Add(TagUtils.BuildLocation(clientCountry));
        var isPremium = clientTags.Contains(TokenRegisteredTags.Premium) || !accessToken.IsPublic;

        // all policies applied to none premium users
        DateTime? expirationTime = null;
        var adRequirement = accessToken.AdRequirement;
        string[]? allowedLocations = null;
        var serverLocationInfo = ServerLocationInfo.Parse(sessionRequestEx.ServerLocation ?? "*");

        if (!isPremium) {
            var planId = sessionRequestEx.Plan ?? ConnectPlanIds.Normal; 
            var clientPolicies = accessToken.ClientPoliciesGet();
            var clientPolicy =
                clientPolicies?.FirstOrDefault(x => x.CountryCode.Equals(clientCountry, StringComparison.OrdinalIgnoreCase)) ??
                clientPolicies?.FirstOrDefault(x => x.CountryCode == "*");

            if (clientPolicy != null) {

                // check auto-location
                if (clientPolicy.AutoLocationOnly && !serverLocationInfo.IsAuto())
                    throw new SessionExceptionEx(SessionErrorCode.AccessError, $"You need to set the location as auto. PlanId: {planId}");

                // add allowed locations
                allowedLocations = clientPolicy.FreeLocations;

                // normal plan
                if (planId.Equals(ConnectPlanIds.Normal, StringComparison.OrdinalIgnoreCase)) {
                    if (clientPolicy.Normal is null or < 0)
                        throw new SessionExceptionEx(SessionErrorCode.AccessError, $"The connect plan is not supported. PlanId: {planId}");

                    expirationTime = DateTime.UtcNow.AddMinutes(clientPolicy.Normal.Value);
                    adRequirement = accessToken.AdRequirement;
                }

                // trial plan
                else if (planId.Equals(ConnectPlanIds.Trial, StringComparison.OrdinalIgnoreCase)) {
                    if (clientPolicy.PremiumByTrial is null or < 0)
                        throw new SessionExceptionEx(SessionErrorCode.AccessError, $"The connect plan is not supported. PlanId: {planId}");

                    expirationTime = DateTime.UtcNow.AddMinutes(clientPolicy.PremiumByTrial.Value);
                    adRequirement = AdRequirement.None;
                }

                // Rewarded Ad plan
                else if (planId.Equals(ConnectPlanIds.RewardAd, StringComparison.OrdinalIgnoreCase)) {
                    if (clientPolicy.PremiumByRewardAd is null or < 0)
                        throw new SessionExceptionEx(SessionErrorCode.AccessError, $"The connect plan is not supported. PlanId: {planId}");
                    
                    expirationTime = DateTime.UtcNow.AddMinutes(clientPolicy.PremiumByRewardAd.Value);
                    adRequirement = AdRequirement.Required;
                }

            }
        }

        var serverSelectOptions = new ServerSelectOptions {
            ProjectCache = projectCache,
            ServerFarmCache = serverFarmCache,
            ClientTags = clientTags.ToArray(),
            IncludeIpV6 = sessionRequestEx.IsIpV6Supported == true || sessionRequestEx.HostEndPoint.IsV6(),
            RequestedLocation = ServerLocationInfo.Parse(sessionRequestEx.ServerLocation ?? "*"),
            AllowedLocations = allowedLocations,
            AllowRedirect = sessionRequestEx.AllowRedirect && allowRedirect
        };

        var result = new Result {
            ServerSelectOptions = serverSelectOptions,
            ExpirationTime = expirationTime,
            AdRequirement = adRequirement,
            IsPremium = isPremium
        };

        return result;
    }
}