using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Contracts.App;
using VpnHood.AppLib.Contracts.ClientProfiles;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.ClientProfiles;

// A profile as the UIs read it: plain values taken off the stored profile once, so the same object
// is read back by a UI on the other side of the API. The client country is baked in - the policy
// and the locations depend on it - which is why ClientProfileService keeps one per profile and
// region. The token stays here: the contract carries what the token yields, never the token.
internal static class ClientProfileInfoBuilder
{
    // What the engine's ServerLocationInfo.Equals did, now that the contract's shape is flat: the
    // stored selection is normalized through the engine's parser before it is compared, so "us/ca",
    // "US/CA" and "US/CA [#premium]" all still name the same location, and a null selection means
    // the automatic one.
    private static bool LocationEquals(ServerLocationItem locationInfo, string? serverLocation)
    {
        return serverLocation is null
            ? locationInfo.IsAuto
            : locationInfo.ServerLocation == ServerLocationInfo.TryParse(serverLocation)?.ServerLocation;
    }

    public static ClientProfileInfo Build(ClientProfile clientProfile, AppFeatures appFeatures)
    {
        var token = clientProfile.Token;
        var clientPolicy = FindClientPolicy(token);
        var locationInfos = ServerLocationItemBuilder.Build(clientProfile, appFeatures);

        // the selected location, else the automatic one, else the first
        var selectedLocationInfo =
            locationInfos.FirstOrDefault(x => LocationEquals(x, clientProfile.SelectedLocation)) ??
            locationInfos.FirstOrDefault(x => x.IsAuto) ??
            locationInfos.FirstOrDefault();

        return new ClientProfileInfo {
            ClientProfileId = clientProfile.ClientProfileId,
            ClientProfileName = GetTitle(clientProfile),
            SupportId = token.SupportId,
            CustomData = clientProfile.CustomData,
            IsPremiumLocationSelected = clientProfile.IsPremiumLocationSelected,
            IsPremium = clientProfile.IsPremium,
            TokenId = token.TokenId,
            HostNames = GetEndPoints(token.ServerToken),
            IsValidHostName = token.ServerToken.IsValidHostName,
            IsBuiltIn = clientProfile.IsBuiltIn,
            AccessCode = AccessCodeUtils.Redact(clientProfile.AccessCode),
            AccessCodeRefusal = clientProfile.AccessCodeRefusal,
            LocationInfos = locationInfos,
            CanGoPremium = clientPolicy?.PremiumByCode == true || clientPolicy?.PremiumByPurchase == true,
            CanTryPremium = clientPolicy?.PremiumByTrial != null,
            CanImportAccessCode = clientPolicy?.PremiumByCode == true && appFeatures.Premium?.AllowImportAccessCode == true,
            CanViewAccessCode = clientPolicy?.PremiumByCode == true,
            CustomServerEndpoints = clientProfile.CustomServerEndpoints,
            IsCustomServerEndpointsEnabled = clientProfile.IsCustomServerEndpointsEnabled,
            SelectedLocationInfo = selectedLocationInfo,
            ClientPolicy = clientPolicy
        };
    }

    // the policy for the client's country, else the one for any country
    private static ClientPolicy? FindClientPolicy(Token token)
    {
        var countryCode = AppRegionInfo.CurrentRegion.Name;
        return token.ClientPolicies?.FirstOrDefault(x =>
                   x.ClientCountries.Any(y => y.Equals(countryCode, StringComparison.OrdinalIgnoreCase))) ??
               token.ClientPolicies?.FirstOrDefault(x => x.ClientCountries.Any(y => y == "*"));
    }

    private static string GetTitle(ClientProfile clientProfile)
    {
        var token = clientProfile.Token;

        if (!string.IsNullOrWhiteSpace(clientProfile.ClientProfileName))
            return clientProfile.ClientProfileName;

        if (!string.IsNullOrWhiteSpace(token.Name))
            return token.Name;

        if (token.ServerToken is { IsValidHostName: false, HostEndPoints.Length: > 0 })
            return Redactor.Always.RedactEndPoint(token.ServerToken.HostEndPoints.First());

        return Redactor.Always.RedactHostName(token.ServerToken.HostName);
    }

    private static string[] GetEndPoints(ServerToken serverToken)
    {
        var hostNames = new List<string>();
        if (serverToken.IsValidHostName)
            hostNames.Add(Redactor.Always.RedactHostName(serverToken.HostName));

        if (serverToken.HostEndPoints != null)
            hostNames.AddRange(serverToken.HostEndPoints.Select(x => Redactor.Always.RedactIpAddress(x.Address)));

        return [.. hostNames];
    }
}
