using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Models
{
    public class Access
    {
        public Guid AccessId { get; set; }
        public Guid AccessTokenId { get; set; }
        public Guid? DeviceId { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime? EndTime { get; set; }

        public virtual AccessToken? AccessToken { get; set; }
        public virtual Device? Device { get; set; }
        [JsonIgnore] public virtual ICollection<AccessUsageEx>? AccessUsages { get; set; }
    }
}