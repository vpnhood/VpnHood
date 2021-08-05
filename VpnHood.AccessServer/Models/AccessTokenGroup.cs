using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

#nullable disable

namespace VpnHood.AccessServer.Models
{
    public partial class AccessTokenGroup
    {
        public Guid ProjectId { get; set; }
        public Guid AccessTokenGroupId { get; set; }
        public string AccessTokenGroupName { get; set; }
        public bool IsDefault { get; set; }

        public virtual Project Project { get; set; }
        
        [JsonIgnore]
        public virtual ICollection<AccessToken> AccessTokens { get; set; } = new HashSet<AccessToken>();
        
        [JsonIgnore]
        public virtual ICollection<ServerEndPoint> ServerEndPoints { get; set; } = new HashSet<ServerEndPoint>();
    }
}
