using System;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server.Configurations;

public class SessionOptions
{
    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? Timeout { get; set; }

    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? UdpTimeout { get; set; }

    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? TcpTimeout { get; set; }

    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? IcmpTimeout { get; set; }

    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? SyncInterval { get; set; }

    public long? SyncCacheSize { get; set; }
    public int? MaxDatagramChannelCount { get; set; }
    public int? MaxUdpPortCount { get; set; }
    public int? TcpBufferSize { get; set; }
    public TimeSpan? TcpConnectTimeout { get; set; }
    public int? MaxTcpConnectWaitCount { get; set; }
    public int? MaxTcpChannelCount { get; set; }
    public int? NetScanLimit { get; set; }
    public TimeSpan? NetScanTimeout { get; set; }
    public bool? UseUdpProxy2 { get; set; }

    [JsonIgnore] public TimeSpan TimeoutValue => Timeout ?? TimeSpan.FromMinutes(60);
    [JsonIgnore] public TimeSpan UdpTimeoutValue => UdpTimeout ?? TimeSpan.FromMinutes(1);
    [JsonIgnore] public TimeSpan TcpTimeoutValue => TcpTimeout ?? TimeSpan.FromMinutes(15);
    [JsonIgnore] public TimeSpan IcmpTimeoutValue => IcmpTimeout ?? TimeSpan.FromSeconds(30);
    [JsonIgnore] public TimeSpan SyncIntervalValue => SyncInterval ?? TimeSpan.FromMinutes(20);
    [JsonIgnore] public long SyncCacheSizeValue => SyncCacheSize ?? 100 * 1000000; // 100 MB
    [JsonIgnore] public int MaxDatagramChannelCountValue => MaxDatagramChannelCount ?? 8;
    [JsonIgnore] public int MaxUdpPortCountValue => MaxUdpPortCount ?? 500;
    [JsonIgnore] public TimeSpan TcpConnectTimeoutValue => TcpConnectTimeout ?? TimeSpan.FromSeconds(60);
    [JsonIgnore] public int MaxTcpConnectWaitCountValue => MaxTcpConnectWaitCount ?? 500;
    [JsonIgnore] public int MaxTcpChannelCountValue => MaxTcpChannelCount ?? 1000;
    [JsonIgnore] public bool UseUdpProxy2Value => UseUdpProxy2 ?? false;
}
