using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;

#nullable disable

namespace VpnHood.AccessServer.Models
{
    public partial class ServerEndPointGroup
    {
        public ServerEndPointGroup()
        {
            AccessTokens = new HashSet<AccessToken>();
            ServerEndPoints = new HashSet<ServerEndPoint>();
        }

        public Guid AccountId { get; set; }
        public Guid ServerEndPointGroupId { get; set; }
        public string ServerEndPointGroupName { get; set; }
        public bool IsDefault { get; set; }

        public virtual Account Account { get; set; }
        public virtual ICollection<AccessToken> AccessTokens { get; set; }
        public virtual ICollection<ServerEndPoint> ServerEndPoints { get; set; }
    }
}
