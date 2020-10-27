using System;
using System.Net;

namespace VpnHood.Server
{
    public class ClientIdentity
    {
        public Guid TokenId { get; set; }
        public string UserId { get; set; }
        public Guid ClientId { get; set; }
        public IPAddress ClientIp { get; set; }
    }
}
