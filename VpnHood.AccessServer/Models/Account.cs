#nullable disable

using System;
using System.Collections.Generic;

namespace VpnHood.AccessServer.Models
{
    //todo: rename to project
    public partial class Account
    {
        public Guid AccountId { get; set; }

        public virtual ICollection<Server> Servers { get; set; } = new HashSet<Server>();
        public virtual ICollection<AccessTokenGroup> AccessTokenGroups { get; set; } = new HashSet<AccessTokenGroup>();
        public virtual ICollection<ServerEndPoint> ServerEndPoints { get; set; } = new HashSet<ServerEndPoint>();
        public virtual ICollection<AccessToken> AccessTokens { get; set; } = new HashSet<AccessToken>();
    }
}
