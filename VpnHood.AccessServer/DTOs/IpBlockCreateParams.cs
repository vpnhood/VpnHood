using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.AccessServer.DTOs
{
    public class IpBlockCreateParams
    {
        [JsonConverter(typeof(IPAddressConverter))]
        public IPAddress IpAddress { get; set; }

        public bool IsBlocked { get; set; }
        public string? Description { get; set; }

        public IpBlockCreateParams(IPAddress ipAddress)
        {
            IpAddress = ipAddress;
        }
    }
}