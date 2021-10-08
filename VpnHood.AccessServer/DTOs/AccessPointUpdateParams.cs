using System;

namespace VpnHood.AccessServer.DTOs
{
    public class AccessPointUpdateParams
    {
        public Wise<Guid>? AccessPointGroupId { get; set; }
        public Wise<string>? PublicIpAddress { get; set; }
        public Wise<string>? PrivateIpAddress { get; set; }
        public Wise<int>? TcpPort { get; set; }
        public Wise<int>? UdpPort { get; set; }
        public Wise<bool>? IncludeInAccessToken { get; set; }
    }
}