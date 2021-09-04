using System;

namespace VpnHood.AccessServer.DTOs
{
    public class ServerEndPointUpdateParams
    {
        public Wise<Guid>? AccessTokenGroupId { get; set; }
        public Wise<string?>? PrivateEndPoint { get; set; }
        public Wise<bool>? MakeDefault { get; set; }
    }
}