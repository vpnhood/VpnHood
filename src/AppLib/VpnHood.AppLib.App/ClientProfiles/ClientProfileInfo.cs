using System.Net;
using System.Text.Json.Serialization;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Abstractions.Device;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Converters;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.ClientProfiles;

// A profile as the UIs read it: plain values taken off the profile once (Create), so the same
// object is read back by a UI on the other side of the API. The client country is baked in - the
// policy and the locations depend on it - which is why ClientProfileService keeps one per profile
// and region.
public class ClientProfileInfo
{
    public required Guid ClientProfileId { get; init; }
    public required string ClientProfileName { get; init; }
    public required string? SupportId { get; init; }
    public required string? CustomData { get; init; }
    public required bool IsPremiumLocationSelected { get; init; }
    public required bool IsPremium { get; init; }
    public required string TokenId { get; init; }
    public required string[] HostNames { get; init; }
    public required bool IsValidHostName { get; init; }
    public required bool IsBuiltIn { get; init; }
    public required string? AccessCode { get; init; }
    public required AccessCodeRefusal? AccessCodeRefusal { get; init; }
    public required ClientServerLocationInfo[] LocationInfos { get; init; }
    public required bool CanGoPremium { get; init; }
    public required bool CanTryPremium { get; init; }

    /// <summary>
    /// May a code be TYPED IN on this profile at all (keyring plan §8)? The operator's policy AND
    /// this build's own capability — one store forbids unlocking with a code entirely, which is why
    /// <see cref="AppPremiumOptions.AllowImportAccessCode" /> defaults to false.
    /// <para>
    /// Deliberately NOT the location's <c>PremiumByCode</c>: that one returns early for a profile
    /// which is already premium, because it answers "can this person UPGRADE". Change code is only
    /// ever offered to somebody who already HAS premium, so reading it there hides the button from
    /// exactly the people it exists for.
    /// </para>
    /// </summary>
    public required bool CanImportAccessCode { get; init; }

    /// <summary>
    /// May the code this device already holds be SHOWN? The operator's policy alone — never
    /// <see cref="AppPremiumOptions.AllowImportAccessCode" />, which answers a different question.
    /// A store may forbid unlocking the app with a typed code without forbidding a buyer from
    /// reading the credential their own purchase produced: it is what they carry to their Android
    /// or Windows device, where typing it IS allowed. So an App Store build shows the code and
    /// offers no box to type one — the same person, premium on every device they own.
    /// </summary>
    public required bool CanViewAccessCode { get; init; }

    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public required IPEndPoint[]? CustomServerEndpoints { get; init; }

    public required bool IsCustomServerEndpointsEnabled { get; init; }
    public required ClientServerLocationInfo? SelectedLocationInfo { get; init; }
    public required ClientPolicy? ClientPolicy { get; init; }

    public bool HasMultipleRegion(string countryCode)
    {
        return LocationInfos.Any(x => x.IsNestedCountry && x.CountryCode == countryCode);
    }

    public static ClientProfileInfo Create(ClientProfile clientProfile, AppFeatures appFeatures)
    {
        var token = clientProfile.Token;
        var clientPolicy = FindClientPolicy(token);
        var locationInfos = ClientServerLocationInfo.CreateFromToken(clientProfile, appFeatures);

        // the selected location, else the automatic one, else the first
        var selectedLocationInfo =
            locationInfos.FirstOrDefault(x => x.LocationEquals(clientProfile.SelectedLocation)) ??
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
