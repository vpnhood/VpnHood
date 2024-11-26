using VpnHood.Common.Tokens;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.ClientProfiles;

public class ClientProfileInfo(ClientProfile clientProfile)
    : ClientProfileBaseInfo(clientProfile)
{
    public string TokenId => ClientProfile.Token.TokenId;
    public string[] HostNames => GetEndPoints(ClientProfile.Token.ServerToken);
    public bool IsValidHostName => ClientProfile.Token.ServerToken.IsValidHostName;
    public bool IsBuiltIn => ClientProfile.IsBuiltIn;
    public bool IsForAccount => ClientProfile.IsForAccount;
    public ClientServerLocationInfo[] LocationInfos { get; } = ClientServerLocationInfo.CreateFromToken(clientProfile.Token);
    public ClientServerLocationInfo? SelectedLocationInfo {
        get {
            var ret =
                LocationInfos.FirstOrDefault(x => x.LocationEquals(ClientProfile.SelectedLocation)) ??
                LocationInfos.FirstOrDefault(x => x.IsAuto) ??
                LocationInfos.FirstOrDefault();

            return ret;
        }
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