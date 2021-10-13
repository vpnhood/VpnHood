using System;

namespace VpnHood.AccessServer.DTOs
{
    public class ServerUpdateParams
    {
        public Wise<string>? ServerName { get; set; }
        public Wise<Guid?>? AccessPointGroupId { get; set; }
        public Wise<bool>? GenerateNewSecret { get; set; }
    }
}