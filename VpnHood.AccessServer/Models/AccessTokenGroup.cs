using System;

namespace VpnHood.AccessServer.Models
{
    public class AccessTokenGroup
    {
        public Guid ProjectId { get; set; }
        public Guid AccessTokenGroupId { get; set; }
        public string? AccessTokenGroupName { get; set; }
        public bool IsDefault { get; set; }

        //public virtual Project? Project { get; set; }
    }
}