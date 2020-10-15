using System;
using System.Text.Json.Serialization;

namespace VpnHood.Server
{
    public class TokenInfo
    {
        public long MaxTransferByteCount { get; set; }
        public long SentByteCount { get; set; }
        public long ReceivedByteCount { get; set; }
        public DateTime? ExpirationTime { get; set; }
        public int MaxClientCount { get; set; } = 1;
        [JsonIgnore]
        public Token Token { get; set; }
    }
}