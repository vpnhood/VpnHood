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
    public ClientServerLocationInfo[] ClientServerLocationInfos { get; } = ClientServerLocationInfo.CreateFromToken(clientProfile.Token);

    public string? SelectedLocation {
        get {
            var serverLocation =
                ClientServerLocationInfos.FirstOrDefault(x => x.LocationEquals(ClientProfile.SelectedLocation))?.ServerLocation ??
                ClientServerLocationInfos.FirstOrDefault(x => x.IsAuto())?.ServerLocation ??
                ClientServerLocationInfos.FirstOrDefault()?.ServerLocation;

            return serverLocation;
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