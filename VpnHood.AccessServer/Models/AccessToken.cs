﻿using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

#nullable disable

namespace VpnHood.AccessServer.Models
{
    public partial class AccessToken
    {
        public Guid ProjectId { get; set; }
        public Guid AccessTokenId { get; set; }
        public string AccessTokenName { get; set; }
        public int SupportCode { get; set; }
        public byte[] Secret { get; set; }
        public Guid AccessTokenGroupId { get; set; }
        public long MaxTraffic { get; set; }
        public int Lifetime { get; set; }
        public int MaxClient { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Url { get; set; }
        public bool IsPublic { get; set; }

        public virtual Project Account { get; set; }
        public virtual AccessTokenGroup AccessTokenGroup { get; set; }
        
        [JsonIgnore]
        public virtual ICollection<AccessUsage> AccessUsages { get; set; } = new HashSet<AccessUsage>();
    }
}
