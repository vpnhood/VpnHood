using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DTOs
{
    public class AccessTokenData
    {
        public AccessToken AccessToken { get; set; } = null!;
        public Usage? Usage { get; set; } 
        public AccessUsageEx? LastAccessUsage { get; set; }
    }
}