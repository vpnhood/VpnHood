using System;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server
{
    public class SessionOptions
    {
        [JsonConverter(typeof(TimeSpanConverter))]
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);

        [JsonConverter(typeof(TimeSpanConverter))]
        public TimeSpan UdpTimeout { get; set; } = TimeSpan.FromMinutes(1);

        [JsonConverter(typeof(TimeSpanConverter))]
        public TimeSpan TcpTimeout { get; set; } = TimeSpan.FromMinutes(15);

        [JsonConverter(typeof(TimeSpanConverter))]
        public TimeSpan IcmpTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public long SyncCacheSize { get; set; } = 100 * 1000000; // 100 MB
        public int MaxDatagramChannelCount { get; set; }
        public int MaxUdpPortCount { get; set; }
        public int TcpBufferSize { get; set; }
    }
}