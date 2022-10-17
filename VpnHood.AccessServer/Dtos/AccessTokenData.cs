using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Dtos;

public class AccessTokenData
{
    public AccessToken AccessToken { get; set; } = default!;
    public Usage? Usage { get; set; } 
    public Access? Access { get; set; }
}