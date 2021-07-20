using System;
using System.Text.Json.Serialization;

namespace VpnHood.Server
{
    public class Access
    {
        public string AccessId { get; set; }
        public byte[] Secret { get; set; }
        public string DnsName { get; set; }
        public DateTime? ExpirationTime { get; set; }
        public int MaxClientCount { get; set; }
        public long MaxTrafficByteCount { get; set; }
        public long SentTrafficByteCount { get; set; }
        public long ReceivedTrafficByteCount { get; set; }
        public string Message { get; set; }
        public AccessStatusCode StatusCode { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string RedirectServerEndPoint { get; set; }

    }
}