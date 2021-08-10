using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Controllers.DTOs
{
    public class AccessTokenData
    {
        public AccessToken AccessToken { get; set; }
        /// <summary>
        /// The usage of the token or null if AccessToken is public.
        /// </summary>
        public AccessUsage AccessUsage { get; set; }
    }

}
