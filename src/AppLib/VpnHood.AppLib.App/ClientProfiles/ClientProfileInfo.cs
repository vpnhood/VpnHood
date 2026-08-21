using System.Net;
using System.Text.Json.Serialization;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Abstractions.Device;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Converters;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.ClientProfiles;

public class ClientProfileInfo(ClientProfile clientProfile, AppFeatures appFeatures)
{
    public Guid ClientProfileId => clientProfile.ClientProfileId;
    public string ClientProfileName => GetTitle();
    public string? SupportId => clientProfile.Token.SupportId;
    public string? CustomData => clientProfile.CustomData;
    public bool IsPremiumLocationSelected => clientProfile.IsPremiumLocationSelected;
    public bool IsPremium => clientProfile.IsPremium;
    public string TokenId => clientProfile.Token.TokenId;
    public string[] HostNames => GetEndPoints(clientProfile.Token.ServerToken);
    public bool IsValidHostName => clientProfile.Token.ServerToken.IsValidHostName;
    public bool IsBuiltIn => clientProfile.IsBuiltIn;
    public string? AccessCode => AccessCodeUtils.Redact(clientProfile.AccessCode);
    public AccessCodeRefusal? AccessCodeRefusal => clientProfile.AccessCodeRefusal;
    public ClientServerLocationInfo[] LocationInfos => ClientServerLocationInfo.CreateFromToken(clientProfile, appFeatures);
    public bool CanGoPremium => ClientPolicy?.PremiumByCode == true || ClientPolicy?.PremiumByPurchase == true;
    public bool CanTryPremium => ClientPolicy?.PremiumByTrial != null;

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
    public bool CanImportAccessCode =>
        ClientPolicy?.PremiumByCode == true && appFeatures.Premium?.AllowImportAccessCode == true;

    /// <summary>
    /// May the code this device already holds be SHOWN? The operator's policy alone — never
    /// <see cref="AppPremiumOptions.AllowImportAccessCode" />, which answers a different question.
    /// A store may forbid unlocking the app with a typed code without forbidding a buyer from
    /// reading the credential their own purchase produced: it is what they carry to their Android
    /// or Windows device, where typing it IS allowed. So an App Store build shows the code and
    /// offers no box to type one — the same person, premium on every device they own.
    /// </summary>
    public bool CanViewAccessCode => ClientPolicy?.PremiumByCode == true;

    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[]? CustomServerEndpoints => clientProfile.CustomServerEndpoints;

    public bool IsCustomServerEndpointsEnabled => clientProfile.IsCustomServerEndpointsEnabled;

    public ClientServerLocationInfo? SelectedLocationInfo {
        get {
            var ret =
                LocationInfos.FirstOrDefault(x => x.LocationEquals(clientProfile.SelectedLocation)) ??
                LocationInfos.FirstOrDefault(x => x.IsAuto) ??
                LocationInfos.FirstOrDefault();

            return ret;
        }
    }
    public ClientPolicy? ClientPolicy => _clientPolicy.Value;

    private readonly Lazy<ClientPolicy?> _clientPolicy = new(() => {
        var countryCode = AppRegionInfo.CurrentRegion.Name;
        return clientProfile.Token.ClientPolicies?.FirstOrDefault(x => 
                   x.ClientCountries.Any(y => y.Equals(countryCode, StringComparison.OrdinalIgnoreCase))) ??
               clientProfile.Token.ClientPolicies?.FirstOrDefault(x => x.ClientCountries.Any(y => y == "*"));
    });

    private string GetTitle()
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

    public bool HasMultipleRegion(string countryCode)
    {
        return LocationInfos.Any(x => x.IsNestedCountry && x.CountryCode == countryCode);
    }
}