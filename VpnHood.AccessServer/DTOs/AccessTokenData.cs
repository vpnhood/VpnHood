using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DTOs
{
    public class AccessTokenData
    {
        public AccessToken AccessToken { get; set; } = null!;
        public AccessTokenUsage Usage { get; set; } = null!;
    }
}