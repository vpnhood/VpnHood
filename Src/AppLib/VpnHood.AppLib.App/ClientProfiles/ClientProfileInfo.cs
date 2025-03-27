using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.ClientProfiles;

public class ClientProfileInfo(ClientProfile clientProfile)
{
    public Guid ClientProfileId => clientProfile.ClientProfileId;
    public string ClientProfileName => GetTitle();
    public string? SupportId => clientProfile.Token.SupportId;
    public string? CustomData => clientProfile.CustomData;
    public bool IsPremiumLocationSelected => clientProfile.IsPremiumLocationSelected;
    public bool IsPremiumAccount => clientProfile.IsPremium;
    public string TokenId => clientProfile.Token.TokenId;
    public string[] HostNames => GetEndPoints(clientProfile.Token.ServerToken);
    public bool IsValidHostName => clientProfile.Token.ServerToken.IsValidHostName;
    public bool IsBuiltIn => clientProfile.IsBuiltIn;
    public bool IsForAccount => clientProfile.IsForAccount;
    public string? AccessCode => AccessCodeUtils.Redact(clientProfile.AccessCode);
    public ClientServerLocationInfo[] LocationInfos => ClientServerLocationInfo.CreateFromToken(clientProfile);
    public Uri? PurchaseUrl => ClientPolicy?.PurchaseUrl;
    public bool AlwaysShowPurchaseUrl => ClientPolicy?.AlwaysShowPurchaseUrl ?? false;

    public ClientServerLocationInfo? SelectedLocationInfo {
        get {
            var ret =
                LocationInfos.FirstOrDefault(x => x.LocationEquals(clientProfile.SelectedLocation)) ??
                LocationInfos.FirstOrDefault(x => x.IsAuto) ??
                LocationInfos.FirstOrDefault();

            return ret;
        }
    }

    private ClientPolicy? ClientPolicy {
        get {
            var countryCode = VpnHoodApp.Instance.GetClientCountry();
            return clientProfile.Token.ClientPolicies?.FirstOrDefault(x => 
                       x.ClientCountries.Any(y => y.Equals(countryCode, StringComparison.OrdinalIgnoreCase))) ??
                   clientProfile.Token.ClientPolicies?.FirstOrDefault(x => x.ClientCountries.Any(y => y == "*"));
        }
    }

    private string GetTitle()
    {
        var token = clientProfile.Token;

        if (!string.IsNullOrWhiteSpace(clientProfile.ClientProfileName))
            return clientProfile.ClientProfileName;

        if (!string.IsNullOrWhiteSpace(token.Name))
            return token.Name;

        if (token.ServerToken is { IsValidHostName: false, HostEndPoints.Length: > 0 })
            return VhUtils.RedactEndPoint(token.ServerToken.HostEndPoints.First());

        return VhUtils.RedactHostName(token.ServerToken.HostName);
    }

    private static string[] GetEndPoints(ServerToken serverToken)
    {
        var hostNames = new List<string>();
        if (serverToken.IsValidHostName)
            hostNames.Add(VhUtils.RedactHostName(serverToken.HostName));

        if (serverToken.HostEndPoints != null)
            hostNames.AddRange(serverToken.HostEndPoints.Select(x => VhUtils.RedactIpAddress(x.Address)));

        return hostNames.ToArray();
    }
}