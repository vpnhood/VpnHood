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
            AccessTokenGroups = new HashSet<AccessTokenGroup>();
        }

        public Guid AccountId { get; set; }

        public virtual ICollection<Server> Servers { get; set; }
        public virtual ICollection<AccessTokenGroup> AccessTokenGroups { get; set; }
        public virtual ICollection<ServerEndPoint> ServerEndPoints { get; set; }
        public virtual ICollection<AccessToken> AccessTokens { get; set; }
    }
}
