using System;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server;

public class SessionOptions
{
    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(60);

    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan UdpTimeout { get; set; } = TimeSpan.FromMinutes(1);

    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan TcpTimeout { get; set; } = TimeSpan.FromMinutes(15);

    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan IcmpTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public long SyncCacheSize { get; set; } = 100 * 1000000; // 100 MB

    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromMinutes(20);
    public int? MaxDatagramChannelCount { get; set; } = 8;
    public int? MaxUdpPortCount { get; set; } = 500;
    public int? TcpBufferSize { get; set; }
    public TimeSpan TcpConnectTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public int MaxTcpConnectWaitCount { get; set; } = 500;
    public int MaxTcpChannelCount { get; set; } = 1000;
    public int? NetScanLimit { get; set; }
    public TimeSpan? NetScanTimeout { get; set; }
    public bool UseUdpProxy2 { get; set; }
}