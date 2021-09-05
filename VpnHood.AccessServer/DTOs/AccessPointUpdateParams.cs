using System;

namespace VpnHood.AccessServer.DTOs
{
    public class AccessPointUpdateParams
    {
        public Wise<Guid>? AccessPointGroupId { get; set; }
        public Wise<string?>? PrivateEndPoint { get; set; }
        public Wise<bool>? MakeDefault { get; set; }
    }
}