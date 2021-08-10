﻿using VpnHood.AccessServer.Models;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Controllers.DTOs
{
    public class AccessTokenData
    {
        public AccessToken AccessToken { get; set; }
        
        /// <summary>
        /// The usage of the token or null if AccessToken is public.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public AccessUsage AccessUsage { get; set; }
    }

}
