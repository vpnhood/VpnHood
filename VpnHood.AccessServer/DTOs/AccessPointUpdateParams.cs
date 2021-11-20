using System;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DTOs
{
    public class AccessPointUpdateParams
    {
        public Patch<Guid>? AccessPointGroupId { get; set; }
        public Patch<string>? IpAddress { get; set; }
        public Patch<int>? TcpPort { get; set; }
        public Patch<int>? UdpPort { get; set; }
        public Patch<AccessPointMode>? AccessPointMode { get; set; }
        public Patch<bool>? IsListen { get; set; }
    }
}