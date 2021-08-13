using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Tunneling.Messages
{
    public class HelloResponse : BaseResponse
    {
        public int SessionId { get; set; }
        public string ServerVersion { get; set; } = null!;
        public int ServerProtocolVersion { get; set; }
        public byte[] SessionKey { get; set; } = null!;
        public int UdpPort { get; set; }
        public byte[]? UdpKey { get; set; } = null!;
        public SuppressType SuppressedTo { get; set; }
        public int MaxDatagramChannelCount { get; set; }

        [JsonConverter(typeof(IPAddressConverter))]
        public IPAddress ClientPublicAddress { get; set; } = null!;
    }
}
