using System;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.DTOs
{
    public class Usage
    {
        [JsonIgnore]
        public Guid GroupByKeyId { get; set; }
        
        [JsonIgnore]
        public long LastAccessUsageId { get; set; }
        public long SentTraffic { get; set; }
        public long ReceivedTraffic { get; set; }
        public DateTime? LastTime { get; set; }
        public int DeviceCount { get; set; }
        public int ServerCount { get; set; }
        public int ClientCount { get; set; }
        public int SessionCount { get; set; }
        public int AccessCount { get; set; }
        public int AccessTokenCount { get; set; }
    }
}