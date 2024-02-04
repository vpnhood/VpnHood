using System.Diagnostics.CodeAnalysis;
using VpnHood.Common;
using VpnHood.Common.Utils;


namespace VpnHood.Client.App;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class ClientProfileInfo(ClientProfile clientProfile)
{
    public Guid ClientProfileId { get; private set; } = clientProfile.ClientProfileId;
    public string ClientProfileName { get; private set; } = GetTitle(clientProfile);
    public string TokenId { get; private set; } = clientProfile.Token.TokenId;
    public string? SupportId { get; private set; } = clientProfile.Token.SupportId;
    public string[] HostNames { get; private set; } = GetEndPoints(clientProfile.Token.ServerToken);
    public bool IsValidHostName { get; private set; } = clientProfile.Token.ServerToken.IsValidHostName;

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