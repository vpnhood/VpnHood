#nullable disable

using System;
using System.Collections.Generic;

namespace VpnHood.AccessServer.Models
{
    public partial class Account
    {
        public Account()
        {
            Servers = new HashSet<Server>();
            ServerEndPointGroups = new HashSet<ServerEndPointGroup>();
        }

        public Guid AccountId { get; set; }

        public virtual ICollection<Server> Servers { get; set; }
        public virtual ICollection<ServerEndPointGroup> ServerEndPointGroups { get; set; }
        public virtual ICollection<ServerEndPoint> ServerEndPoints { get; set; }
    }
}
