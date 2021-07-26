using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Tunneling.Messages
{
    public class HelloResponse : BaseResponse
    {
        public int SessionId { get; set; }
        public string ServerVersion { get; set; }
        public int ServerProtocolVersion { get; set; }
        public byte[] SessionKey { get; set; }
        public int UdpPort { get; set; }
        public byte[] UdpKey { get; set; }
        public SuppressType SuppressedTo { get; set; }
        public int MaxDatagramChannelCount { get; set; }
        
        [JsonConverter(typeof(IPAddressConverter))]
        public IPAddress ClientPublicAddress { get; set; }
    }
}
