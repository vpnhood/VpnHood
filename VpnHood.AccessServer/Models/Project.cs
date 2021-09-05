using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Models
{
    public class Project
    {
        public Guid ProjectId { get; set; }

        [JsonIgnore] public virtual ICollection<Server>? Servers { get; set; }

        [JsonIgnore] public virtual ICollection<AccessPointGroup>? AccessPointGroups { get; set; }

        [JsonIgnore] public virtual ICollection<AccessPoint>? AccessPoints { get; set; }

        [JsonIgnore] public virtual ICollection<AccessToken>? AccessTokens { get; set; }

        [JsonIgnore] public virtual ICollection<ProjectClient>? Clients { get; set; }
    }
}