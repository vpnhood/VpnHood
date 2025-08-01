﻿using System.Text.Json.Serialization;
using VpnHood.Core.Common.Messaging;
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
    public TransferBufferSize? TcpKernelBufferSize { get; set; }
    public TransferBufferSize? StreamProxyBufferSize { get; set; }
    public TransferBufferSize? UdpProxyBufferSize { get; set; }
    public TransferBufferSize? UdpChannelBufferSize { get; set; }
    public TimeSpan? TcpConnectTimeout { get; set; }
    public TimeSpan? TcpReuseTimeout { get; set; }
    public int? MaxTcpConnectWaitCount { get; set; }
    public int? MaxTcpChannelCount { get; set; }
    public int? NetScanLimit { get; set; }
    public TimeSpan? NetScanTimeout { get; set; }
    public bool? UseUdpProxy2 { get; set; }

    [JsonIgnore] public TimeSpan TimeoutValue => Timeout ?? TimeSpan.FromMinutes(30);
    [JsonIgnore] public TimeSpan UdpTimeoutValue => UdpTimeout ?? TimeSpan.FromMinutes(1);
    [JsonIgnore] public TransferBufferSize? UdpChannelBufferSizeValue => UdpChannelBufferSize ?? null;
    [JsonIgnore] public TransferBufferSize? UdpProxyBufferSizeValue => UdpProxyBufferSize ?? null;
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
    [JsonIgnore] public bool UseUdpProxy2Value => UseUdpProxy2 ?? true;

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
        if (obj.StreamProxyBufferSize != null) StreamProxyBufferSize = obj.StreamProxyBufferSize;
        if (obj.TcpKernelBufferSize != null) TcpKernelBufferSize = obj.TcpKernelBufferSize;
        if (obj.UdpProxyBufferSize != null) UdpProxyBufferSize = obj.UdpProxyBufferSize;
        if (obj.UdpChannelBufferSize != null) UdpChannelBufferSize = obj.UdpChannelBufferSize;
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
        StreamProxyBufferSize = StreamProxyBufferSize; //no default
        TcpKernelBufferSize = TcpKernelBufferSize; //no default
        UdpProxyBufferSize = UdpProxyBufferSizeValue;
        UdpChannelBufferSize = UdpChannelBufferSizeValue;
        TcpConnectTimeout = TcpConnectTimeoutValue;
        TcpReuseTimeout = TcpReuseTimeoutValue;
        MaxTcpConnectWaitCount = MaxTcpConnectWaitCountValue;
        MaxTcpChannelCount = MaxTcpChannelCountValue;
        NetScanLimit = NetScanLimit;
        NetScanTimeout = NetScanTimeout;
        UseUdpProxy2 = UseUdpProxy2Value;
    }
}