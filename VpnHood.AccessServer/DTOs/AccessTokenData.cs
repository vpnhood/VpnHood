using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DTOs;

public class AccessTokenData
{
    public string? AccessPointGroupName { get; set; } = default!;
    public AccessToken AccessToken { get; set; } = default!;
    public Usage? Usage { get; set; } 
    public AccessUsageEx? LastAccessUsage { get; set; }
}