using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.AccessServer.DTOs
{
    public class IpLockCreateParams
    {
        [JsonConverter(typeof(IPAddressConverter))]
        public IPAddress IpAddress { get; set; }

        public bool IsLocked { get; set; }
        public string? Description { get; set; }

        public IpLockCreateParams(IPAddress ipAddress)
        {
            IpAddress = ipAddress;
        }
    }
}