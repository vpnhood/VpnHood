using VpnHood.Common.Utils;

namespace VpnHood.Client.App.ClientProfiles;

public class ClientProfileBaseInfo(ClientProfile clientProfile)
{
    protected ClientProfile ClientProfile = clientProfile;

    public Guid ClientProfileId => ClientProfile.ClientProfileId;
    public string ClientProfileName => GetTitle(ClientProfile);
    
    public string? SupportId => ClientProfile.Token.SupportId;
    
    public string? CustomData => ClientProfile.CustomData;
    
    public bool IsPremiumLocationSelected => ClientProfile.IsPremiumLocationSelected;
    
    public bool IsPremiumAccount => !ClientProfile.Token.IsPublic;

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