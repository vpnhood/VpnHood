using System.Text.Json.Serialization;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.ClientProfiles;

public class ClientProfileBaseInfo(ClientProfile clientProfile)
{
    public Guid ClientProfileId { get; private set; } = clientProfile.ClientProfileId;
    public string ClientProfileName { get; private set; } = GetTitle(clientProfile);
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? SupportId { get; private set; } = clientProfile.Token.SupportId;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? CustomData { get; private set; } = clientProfile.CustomData;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPremiumLocationSelected { get; private set; } = clientProfile.IsPremiumLocationSelected;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPremiumAccount = !clientProfile.Token.IsPublic;

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