using System;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DTOs
{
    public class AccessPointUpdateParams
    {
        public Wise<Guid>? AccessPointGroupId { get; set; }
        public Wise<string>? IpAddress { get; set; }
        public Wise<int>? TcpPort { get; set; }
        public Wise<int>? UdpPort { get; set; }
        public Wise<AccessPointMode>? AccessPointMode { get; set; }
        public Wise<bool>? IsListen { get; set; }
    }
}