using VpnHood.Common.Tokens;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.ClientProfiles;

public class ClientProfileInfo(ClientProfile clientProfile)
    : ClientProfileBaseInfo(clientProfile) //todo
{
    public string TokenId { get; private set; } = clientProfile.Token.TokenId;
    public string[] HostNames { get; private set; } = GetEndPoints(clientProfile.Token.ServerToken);
    public bool IsValidHostName { get; private set; } = clientProfile.Token.ServerToken.IsValidHostName;
    public bool IsBuiltIn { get; private set; } = clientProfile.IsBuiltIn;
    public bool IsForAccount { get; private set; } = clientProfile.IsForAccount;
    public ClientServerLocationInfo[] ClientServerLocationInfos { get; } = ClientServerLocationInfo.CreateFromToken(clientProfile.Token);

    public string? SelectedLocation {
        get {
            var serverLocation =
                ClientServerLocationInfos.FirstOrDefault(x => x.LocationEquals(clientProfile.SelectedLocation))?.ServerLocation ??
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