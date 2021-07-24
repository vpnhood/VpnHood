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

        public string ServerEndPointGroupName { get; set; }
        public int ServerEndPointGroupId { get; set; }

        public virtual ICollection<AccessToken> AccessTokens { get; set; }
        public virtual ICollection<ServerEndPoint> ServerEndPoints { get; set; }
    }
}
