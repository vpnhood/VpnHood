using VpnHood.AccessServer.Report.Views;

namespace VpnHood.AccessServer.Dtos.AccessToken;

public class AccessTokenData(AccessToken accessToken)
{
    public AccessToken AccessToken { get; set; } = accessToken;
    public Usage? Usage { get; set; }
    public Access? Access { get; set; }
}