﻿using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using VpnHood.AccessServer.Authorization.Models;

namespace VpnHood.AccessServer.Models
{
    public class Project
    {
        public Guid ProjectId { get; set; }
        public string? ProjectName { get; set; }

        [JsonIgnore] public virtual ICollection<Server>? Servers { get; set; }

        [JsonIgnore] public virtual ICollection<AccessPointGroup>? AccessPointGroups { get; set; }

        [JsonIgnore] public virtual ICollection<AccessToken>? AccessTokens { get; set; }

        [JsonIgnore] public virtual ICollection<ProjectClient>? Clients { get; set; }

        [JsonIgnore] public virtual ICollection<ProjectRole>? ProjectRoles { get; set; }
    }
}