﻿using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Models
{
    public class AccessToken
    {
        public Guid ProjectId { get; set; }
        public Guid AccessTokenId { get; set; }
        public string? AccessTokenName { get; set; }
        public int SupportCode { get; set; }
        public byte[] Secret { get; set; } = null!;
        public Guid AccessPointGroupId { get; set; }
        public long MaxTraffic { get; set; }
        public int Lifetime { get; set; }
        public int MaxClient { get; set; }
        public DateTime? StartTime { get; set; } = null!;
        public DateTime? EndTime { get; set; } = null!;
        public string? Url { get; set; }
        public bool IsPublic { get; set; }

        public virtual Project? Project { get; set; }
        public virtual AccessPointGroup? AccessPointGroup { get; set; }

        [JsonIgnore] public virtual ICollection<Session>? Sessions { get; set; }
    }
}