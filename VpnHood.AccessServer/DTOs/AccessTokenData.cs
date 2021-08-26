using System.Text.Json.Serialization;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DTOs
{
    public class AccessTokenData
    {
        public AccessToken AccessToken { get; set; } = null!;

        /// <summary>
        /// The usage of the token or null if AccessToken is public.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public AccessUsage? AccessUsage { get; set; }
    }

}
