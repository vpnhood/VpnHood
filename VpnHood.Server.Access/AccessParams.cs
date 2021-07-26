using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server
{
    public class AccessParams
    {
        public ClientIdentity ClientIdentity { get; set; }

        [JsonConverter(typeof(IPEndPointConverter))]
        public IPEndPoint RequestEndPoint { get; set; }
    }
}
