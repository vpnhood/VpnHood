using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;

#nullable disable

namespace VpnHood.AccessServer.Models
{
    public partial class AccessTokenGroup
    {
        public AccessTokenGroup()
        {
            AccessTokens = new HashSet<AccessToken>();
            ServerEndPoints = new HashSet<ServerEndPoint>();
        }

        public Guid AccountId { get; set; }
        public Guid AccessTokenGroupId { get; set; }
        public string AccessTokenGroupName { get; set; }
        public bool IsDefault { get; set; }

        public virtual Account Account { get; set; }
        public virtual ICollection<AccessToken> AccessTokens { get; set; }
        public virtual ICollection<ServerEndPoint> ServerEndPoints { get; set; }
    }
}
