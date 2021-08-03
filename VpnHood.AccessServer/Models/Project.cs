#nullable disable

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Models
{
    public partial class Project
    {
        public Guid ProjectId { get; set; }

        [JsonIgnore]
        public virtual ICollection<Server> Servers { get; set; } = new HashSet<Server>();
        
        [JsonIgnore]
        public virtual ICollection<AccessTokenGroup> AccessTokenGroups { get; set; } = new HashSet<AccessTokenGroup>();
        
        [JsonIgnore]
        public virtual ICollection<ServerEndPoint> ServerEndPoints { get; set; } = new HashSet<ServerEndPoint>();
        
        [JsonIgnore]
        public virtual ICollection<AccessToken> AccessTokens { get; set; } = new HashSet<AccessToken>();

        [JsonIgnore]
        public virtual ICollection<Client> Clients { get; set; } = new HashSet<Client>();
    }
}
