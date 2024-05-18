using VpnHood.Common;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.ClientProfiles;

public class ClientProfileBaseInfo(ClientProfile clientProfile)
{
    public Guid ClientProfileId { get; private set; } = clientProfile.ClientProfileId;
    public string ClientProfileName { get; private set; } = GetTitle(clientProfile);
    public string? SupportId { get; private set; } = clientProfile.Token.SupportId;
    public ClientServerLocationInfo[] ServerLocationInfos { get; private set; } = 
        ClientServerLocationInfo.AddCategoryGaps(clientProfile.Token.ServerToken.ServerLocations);

    private static string GetTitle(ClientProfile clientProfile)
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
}