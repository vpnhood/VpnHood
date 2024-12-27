using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Common.Utils;

namespace VpnHood.AppLib.ClientProfiles;

public class ClientProfileInfo(ClientProfile clientProfile)
{
    public Guid ClientProfileId => clientProfile.ClientProfileId;
    public string ClientProfileName => GetTitle();
    public string? SupportId => clientProfile.Token.SupportId;
    public string? CustomData => clientProfile.CustomData;
    public bool IsPremiumLocationSelected => clientProfile.IsPremiumLocationSelected;
    public bool IsPremiumAccount => clientProfile.IsPremium && VpnHoodApp.Instance.Features.IsPremiumFlagSupported; //todo move to UI
    public string TokenId => clientProfile.Token.TokenId;
    public string[] HostNames => GetEndPoints(clientProfile.Token.ServerToken);
    public bool IsValidHostName => clientProfile.Token.ServerToken.IsValidHostName;
    public bool IsBuiltIn => clientProfile.IsBuiltIn;
    public bool IsForAccount => clientProfile.IsForAccount;
    public string? AccessCode => FormantAccessCode(clientProfile.AccessCode);
    public ClientServerLocationInfo[] LocationInfos => ClientServerLocationInfo.CreateFromToken(clientProfile.Token);

    public ClientServerLocationInfo? SelectedLocationInfo {
        get {
            var ret =
                LocationInfos.FirstOrDefault(x => x.LocationEquals(clientProfile.SelectedLocation)) ??
                LocationInfos.FirstOrDefault(x => x.IsAuto) ??
                LocationInfos.FirstOrDefault();

            return ret;
        }
    }

    private static string? FormantAccessCode(string? accessCode)
    {
        if (string.IsNullOrWhiteSpace(accessCode))
            return null;
        
        if (accessCode.Length <= 4)
            return "***";

        // replace all but last 4 characters with '*'
        return new string('*', accessCode.Length - 4) + accessCode[^4..];
    }

    private string GetTitle()
    {
        var token = clientProfile.Token;

        if (!string.IsNullOrWhiteSpace(clientProfile.ClientProfileName))
            return clientProfile.ClientProfileName;

        if (!string.IsNullOrWhiteSpace(token.Name))
            return token.Name;

        if (token.ServerToken is { IsValidHostName: false, HostEndPoints.Length: > 0 })
            return VhUtil.RedactEndPoint(token.ServerToken.HostEndPoints.First());

        return VhUtil.RedactHostName(token.ServerToken.HostName);
    }

    private static string[] GetEndPoints(ServerToken serverToken)
    {
        var hostNames = new List<string>();
        if (serverToken.IsValidHostName)
            hostNames.Add(VhUtil.RedactHostName(serverToken.HostName));

        if (serverToken.HostEndPoints != null)
            hostNames.AddRange(serverToken.HostEndPoints.Select(x => VhUtil.RedactIpAddress(x.Address)));

        return hostNames.ToArray();
    }
}