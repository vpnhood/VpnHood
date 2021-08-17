using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Tunneling.Messages
{
    public class BaseResponse
    {
        public ResponseCode ResponseCode { get; set; }
        public AccessUsage? AccessUsage { get; set; }
        public SuppressType SuppressedBy { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] 
        [JsonConverter(typeof(IPEndPointConverter))]
        public IPEndPoint? RedirectHostEndPoint { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
