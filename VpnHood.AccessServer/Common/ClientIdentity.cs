using System;
using System.Net;

namespace VpnHood.AccessServer
{
    public class ClientIdentity
    {
        public Guid TokenId { get; set; }
        public string UserToken { get; set; }
        public Guid ClientId { get; set; }
        public string ClientIp { get; set; }
    }
}
