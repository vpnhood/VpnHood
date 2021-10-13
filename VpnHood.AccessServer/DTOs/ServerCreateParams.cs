using System;

namespace VpnHood.AccessServer.DTOs
{
    public class ServerCreateParams
    {
        public string? ServerName { get; set; }
        public Guid? AccessPointGroupId { get; set; }
    }
}