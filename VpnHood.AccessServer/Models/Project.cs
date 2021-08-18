using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Models
{
    public partial class Project
    {
        public Guid ProjectId { get; set; }

        [JsonIgnore]
        public virtual ICollection<Server>? Servers { get; set; }
        
        [JsonIgnore]
        public virtual ICollection<AccessTokenGroup>? AccessTokenGroups { get; set; } 
        
        [JsonIgnore]
        public virtual ICollection<ServerEndPoint>? ServerEndPoints { get; set; } 
        
        [JsonIgnore]
        public virtual ICollection<AccessToken>? AccessTokens { get; set; }

        [JsonIgnore]
        public virtual ICollection<Client>? Clients { get; set; } 
    }
}
