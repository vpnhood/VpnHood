using System.Text.Json.Serialization;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.Core.Server.Access.Configurations;

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
    public int? MaxPacketChannelCount { get; set; }
    public int? MaxUdpClientCount { get; set; }
    public int? MaxIcmpClientCount { get; set; }
    public int? TcpBufferSize { get; set; }
    public int? TcpKernelSendBufferSize { get; set; }
    public int? TcpKernelReceiveBufferSize { get; set; }
    public int? UdpProxySendBufferSize { get; set; }
    public int? UdpProxyReceiveBufferSize { get; set; }
    public int? UdpChannelSendBufferSize { get; set; }
    public int? UdpChannelReceiveBufferSize { get; set; }
    public TimeSpan? TcpConnectTimeout { get; set; }
    public TimeSpan? TcpReuseTimeout { get; set; }
    public int? MaxTcpConnectWaitCount { get; set; }
    public int? MaxTcpChannelCount { get; set; }
    public int? NetScanLimit { get; set; }
    public TimeSpan? NetScanTimeout { get; set; }
    public bool? UseUdpProxy2 { get; set; }

    [JsonIgnore] public TimeSpan TimeoutValue => Timeout ?? TimeSpan.FromMinutes(30);
    [JsonIgnore] public TimeSpan UdpTimeoutValue => UdpTimeout ?? TimeSpan.FromMinutes(1);
    [JsonIgnore] public int? UdpChannelSendBufferSizeValue => UdpChannelSendBufferSize ?? null;
    [JsonIgnore] public int? UdpChannelReceiveBufferSizeValue => UdpChannelReceiveBufferSize ?? null;
    [JsonIgnore] public int? UdpProxySendBufferSizeValue => UdpProxySendBufferSize ?? null;
    [JsonIgnore] public int? UdpProxyReceiveBufferSizeValue => UdpProxyReceiveBufferSize ?? null;
    [JsonIgnore] public TimeSpan TcpTimeoutValue => TcpTimeout ?? TimeSpan.FromMinutes(15);
    [JsonIgnore] public TimeSpan IcmpTimeoutValue => IcmpTimeout ?? TimeSpan.FromSeconds(30);
    [JsonIgnore] public TimeSpan SyncIntervalValue => SyncInterval ?? TimeSpan.FromMinutes(20);
    [JsonIgnore] public long SyncCacheSizeValue => SyncCacheSize ?? 100 * 1000000; // 100 MB
    [JsonIgnore] public int MaxPacketChannelCountValue => MaxPacketChannelCount ?? 8;
    [JsonIgnore] public int MaxUdpClientCountValue => MaxUdpClientCount ?? 500;
    [JsonIgnore] public int MaxIcmpClientCountValue => MaxIcmpClientCount ?? 20;
    [JsonIgnore] public TimeSpan TcpConnectTimeoutValue => TcpConnectTimeout ?? TimeSpan.FromSeconds(30);
    [JsonIgnore] public TimeSpan TcpReuseTimeoutValue => TcpReuseTimeout ?? TimeSpan.FromSeconds(40);
    [JsonIgnore] public int MaxTcpConnectWaitCountValue => MaxTcpConnectWaitCount ?? 500;
    [JsonIgnore] public int MaxTcpChannelCountValue => MaxTcpChannelCount ?? 1000;
    [JsonIgnore] public bool UseUdpProxy2Value => UseUdpProxy2 ?? false; //todo: remove

    public void Merge(SessionOptions obj)
    {
        if (obj.Timeout != null) Timeout = obj.Timeout;
        if (obj.UdpTimeout != null) UdpTimeout = obj.UdpTimeout;
        if (obj.TcpTimeout != null) TcpTimeout = obj.TcpTimeout;
        if (obj.IcmpTimeout != null) IcmpTimeout = obj.IcmpTimeout;
        if (obj.SyncInterval != null) SyncInterval = obj.SyncInterval;
        if (obj.SyncCacheSize != null) SyncCacheSize = obj.SyncCacheSize;
        if (obj.MaxPacketChannelCount != null) MaxPacketChannelCount = obj.MaxPacketChannelCount;
        if (obj.MaxUdpClientCount != null) MaxUdpClientCount = obj.MaxUdpClientCount;
        if (obj.MaxIcmpClientCount != null) MaxIcmpClientCount = obj.MaxIcmpClientCount;
        if (obj.TcpBufferSize != null) TcpBufferSize = obj.TcpBufferSize;
        if (obj.TcpKernelSendBufferSize != null) TcpKernelSendBufferSize = obj.TcpKernelSendBufferSize;
        if (obj.TcpKernelReceiveBufferSize != null) TcpKernelReceiveBufferSize = obj.TcpKernelReceiveBufferSize;
        if (obj.UdpProxySendBufferSize != null) UdpProxySendBufferSize = obj.UdpProxySendBufferSize;
        if (obj.UdpProxyReceiveBufferSize != null) UdpProxyReceiveBufferSize = obj.UdpProxyReceiveBufferSize;
        if (obj.UdpChannelSendBufferSize != null) UdpChannelSendBufferSize = obj.UdpChannelSendBufferSize;
        if (obj.UdpChannelReceiveBufferSize != null) UdpChannelReceiveBufferSize = obj.UdpChannelReceiveBufferSize;
        if (obj.TcpConnectTimeout != null) TcpConnectTimeout = obj.TcpConnectTimeout;
        if (obj.TcpReuseTimeout != null) TcpReuseTimeout = obj.TcpReuseTimeout;
        if (obj.MaxTcpConnectWaitCount != null) MaxTcpConnectWaitCount = obj.MaxTcpConnectWaitCount;
        if (obj.MaxTcpChannelCount != null) MaxTcpChannelCount = obj.MaxTcpChannelCount;
        if (obj.NetScanLimit != null) NetScanLimit = obj.NetScanLimit;
        if (obj.NetScanTimeout != null) NetScanTimeout = obj.NetScanTimeout;
        if (obj.UseUdpProxy2 != null) UseUdpProxy2 = obj.UseUdpProxy2;
    }

    public void ApplyDefaults()
    {
        Timeout = TimeoutValue;
        UdpTimeout = UdpTimeoutValue;
        TcpTimeout = TcpTimeoutValue;
        IcmpTimeout = IcmpTimeoutValue;
        SyncInterval = SyncIntervalValue;
        SyncCacheSize = SyncCacheSizeValue;
        MaxPacketChannelCount = MaxPacketChannelCountValue;
        MaxUdpClientCount = MaxUdpClientCountValue;
        MaxIcmpClientCount = MaxIcmpClientCountValue;
        TcpBufferSize = TcpBufferSize; //no default
        TcpKernelSendBufferSize = TcpKernelSendBufferSize; //no default
        TcpKernelReceiveBufferSize = TcpKernelReceiveBufferSize; //no default
        UdpProxySendBufferSize = UdpProxySendBufferSizeValue; //no default
        UdpProxyReceiveBufferSize = UdpProxyReceiveBufferSizeValue; //no default
        UdpChannelSendBufferSize = UdpChannelSendBufferSizeValue;
        UdpChannelReceiveBufferSize = UdpChannelReceiveBufferSizeValue;
        TcpConnectTimeout = TcpConnectTimeoutValue;
        TcpReuseTimeout = TcpReuseTimeoutValue;
        MaxTcpConnectWaitCount = MaxTcpConnectWaitCountValue;
        MaxTcpChannelCount = MaxTcpChannelCountValue;
        NetScanLimit = NetScanLimit;
        NetScanTimeout = NetScanTimeout;
        UseUdpProxy2 = UseUdpProxy2Value;
    }
}