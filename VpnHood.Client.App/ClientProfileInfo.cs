using VpnHood.Common;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App;

public class ClientProfileInfo
{
    public ClientProfileInfo(ClientProfile clientProfile)
    {
        ClientProfileId = clientProfile.ClientProfileId;
        ClientProfileName = GetTitle(clientProfile);
        TokenId = clientProfile.Token.TokenId;
        SupportId = clientProfile.Token.SupportId;
        IsValidHostName = clientProfile.Token.ServerToken.IsValidHostName;
        HostNames = GetEndPoints(clientProfile.Token.ServerToken);
    }

    public Guid ClientProfileId { get; private set; }
    public string ClientProfileName { get; private set; }
    public string TokenId { get; private set; }
    public string? SupportId { get; private set; }
    public string[] HostNames { get; private set; }
    public bool IsValidHostName { get; private set; }

    private static string[] GetEndPoints(ServerToken serverToken)
    {
        var hostNames = new List<string>();
        if (serverToken.IsValidHostName)
            hostNames.Add(VhUtil.RedactHostName(serverToken.HostName));

        if (serverToken.HostEndPoints != null)
            hostNames.AddRange(serverToken.HostEndPoints.Select(x => VhUtil.RedactIpAddress(x.Address)));

        return hostNames.ToArray();
    }

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