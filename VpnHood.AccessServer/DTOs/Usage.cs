using System;

namespace VpnHood.AccessServer.DTOs
{
    public class Usage
    {
        public long SentTraffic { get; set; }
        public long ReceivedTraffic { get; set; }
        public DateTime? LastTime { get; set; }
        public int ServerCount { get; set; }
        public int ClientCount { get; set; }
    }
}