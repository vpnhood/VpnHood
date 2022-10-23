
namespace VpnHood.AccessServer.Dtos;

public class AccessTokenData
{
    public Models.AccessToken AccessToken { get; set; } = default!;
    public Usage? Usage { get; set; } 
    public Access? Access { get; set; }
}