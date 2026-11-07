using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Converters;

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
    public int? MaxUdpDnsPacketsPerSecond { get; set; }
    public int? MaxIcmpClientCount { get; set; }
    public TransferBufferSize? TcpKernelBufferSize { get; set; }
    public TransferBufferSize? StreamForwardingBufferSize { get; set; }

    // the sockets of the UDP forwarders; UdpChannelBufferSize is the tunnel's own UDP channel
    public TransferBufferSize? UdpForwardingBufferSize { get; set; }
    public TransferBufferSize? UdpChannelBufferSize { get; set; }
    public TimeSpan? TcpConnectTimeout { get; set; }
    public TimeSpan? ChannelIdleTimeout { get; set; }
    public int? MaxTcpConnectWaitCount { get; set; }
    public int? MaxTcpChannelCount { get; set; }
    public int? MaxTcpConnectsPerSecond { get; set; }
    public int? NetScanLimit { get; set; }
    public TimeSpan? NetScanTimeout { get; set; }

    // The two ways TCP can go. Forwarding: the client ends each TCP connection on the
    // device and the server opens a new one, passing the bytes between them. Packet: raw IP packets in the tunnel, with
    // TCP running end to end. Turning one off leaves TCP going by the other; both off leave it no way through
    public bool? AllowTcpForwarding { get; set; }
    public bool? AllowTcpPacket { get; set; }

    // Compatibility (2026-11): the names that server profiles, config files and older servers hold. Both are written,
    // and a value under the new name wins. Drop once the access manager serves no server from before them and its
    // stored profiles have moved
    [Obsolete("Use AllowTcpForwarding.")]
    public bool? AllowTcpProxy {
        get => AllowTcpForwarding;
        set => AllowTcpForwarding ??= value;
    }

    [Obsolete("Use StreamForwardingBufferSize.")]
    public TransferBufferSize? StreamProxyBufferSize {
        get => StreamForwardingBufferSize;
        set => StreamForwardingBufferSize ??= value;
    }

    [Obsolete("Use UdpForwardingBufferSize.")]
    public TransferBufferSize? UdpProxyBufferSize {
        get => UdpForwardingBufferSize;
        set => UdpForwardingBufferSize ??= value;
    }

    // Optional cover traffic at the start of WebSocket forwarding streams; never applies to packet channels.
    // Null inherits a layered configuration; explicit false overrides true. The effective default is off.
    public bool? StartPadding { get; set; }

    // The users' own QUIC and UDP inside the tunnel, not the server's UDP and QUIC channels. Turned off, they are
    // enforced only with AllowTcpPacket off, as otherwise the tunnel carries the users' TCP as packets anyway.
    public bool? AllowQuicPacket { get; set; }
    public bool? AllowUdpPacket { get; set; }

    // Raises the minimum accepted HTTP protocol before reading a body or upgrading the connection.
    public int? MinClientProtocolVersion { get; set; }

    // the server's connections that haven't proven a key yet, its port 80's included; above it, the address that
    // holds the most loses its oldest
    public int? MaxUnauthenticatedConnectionCount { get; set; }

    [JsonIgnore]
    public TimeSpan TimeoutValue => Timeout ?? TimeSpan.FromMinutes(30);

    [JsonIgnore]
    public TimeSpan UdpTimeoutValue => UdpTimeout ?? TimeSpan.FromMinutes(1);

    [JsonIgnore]
    public TransferBufferSize? UdpChannelBufferSizeValue => UdpChannelBufferSize ?? null;

    [JsonIgnore]
    public TransferBufferSize? UdpForwardingBufferSizeValue => UdpForwardingBufferSize ?? null;

    [JsonIgnore]
    public TimeSpan TcpTimeoutValue => TcpTimeout ?? TimeSpan.FromMinutes(15);

    [JsonIgnore]
    public TimeSpan IcmpTimeoutValue => IcmpTimeout ?? TimeSpan.FromSeconds(30);

    [JsonIgnore]
    public TimeSpan SyncIntervalValue => SyncInterval ?? TimeSpan.FromMinutes(20);

    [JsonIgnore]
    public long SyncCacheSizeValue => SyncCacheSize ?? 100 * 1000000; // 100 MB

    [JsonIgnore]
    public int MaxPacketChannelCountValue => MaxPacketChannelCount ?? 8;

    [JsonIgnore]
    public int MaxUdpClientCountValue => MaxUdpClientCount ?? 500;

    // a session's DNS over UDP; far above a device's own lookups, far below a flood that gets the server reported
    [JsonIgnore]
    public int MaxUdpDnsPacketsPerSecondValue => MaxUdpDnsPacketsPerSecond ?? 200;

    [JsonIgnore]
    public int MaxIcmpClientCountValue => MaxIcmpClientCount ?? 20;

    [JsonIgnore]
    public TimeSpan TcpConnectTimeoutValue => TcpConnectTimeout ?? TimeSpan.FromSeconds(30);

    [JsonIgnore]
    public TimeSpan ChannelIdleTimeoutValue => ChannelIdleTimeout ?? TimeSpan.FromSeconds(40);

    [JsonIgnore]
    public int MaxTcpConnectWaitCountValue => MaxTcpConnectWaitCount ?? 500;

    [JsonIgnore]
    public int MaxTcpChannelCountValue => MaxTcpChannelCount ?? 1000;

    // a session's new TCP connections; far above a browser's bursts, far below a SYN flood
    [JsonIgnore]
    public int MaxTcpConnectsPerSecondValue => MaxTcpConnectsPerSecond ?? 200;

    // an estimate: a client's connection proves its key within about a second, so the count stays near the
    // server's new connections a second, and a cap well above that leaves a mass reconnect alone
    [JsonIgnore]
    public int MaxUnauthenticatedConnectionCountValue => MaxUnauthenticatedConnectionCount ?? 1000;

    [JsonIgnore]
    public bool AllowTcpForwardingValue => AllowTcpForwarding ?? true;

    [JsonIgnore]
    public bool StartPaddingValue => StartPadding ?? false;

    [JsonIgnore]
    public bool AllowTcpPacketValue => AllowTcpPacket ?? true;

    [JsonIgnore]
    public bool AllowQuicPacketValue => AllowQuicPacket ?? true;

    [JsonIgnore]
    public bool AllowUdpPacketValue => AllowUdpPacket ?? true;

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
        if (obj.MaxUdpDnsPacketsPerSecond != null) MaxUdpDnsPacketsPerSecond = obj.MaxUdpDnsPacketsPerSecond;
        if (obj.MaxIcmpClientCount != null) MaxIcmpClientCount = obj.MaxIcmpClientCount;
        if (obj.StreamForwardingBufferSize != null) StreamForwardingBufferSize = obj.StreamForwardingBufferSize;
        if (obj.TcpKernelBufferSize != null) TcpKernelBufferSize = obj.TcpKernelBufferSize;
        if (obj.UdpForwardingBufferSize != null) UdpForwardingBufferSize = obj.UdpForwardingBufferSize;
        if (obj.UdpChannelBufferSize != null) UdpChannelBufferSize = obj.UdpChannelBufferSize;
        if (obj.TcpConnectTimeout != null) TcpConnectTimeout = obj.TcpConnectTimeout;
        if (obj.ChannelIdleTimeout != null) ChannelIdleTimeout = obj.ChannelIdleTimeout;
        if (obj.MaxTcpConnectWaitCount != null) MaxTcpConnectWaitCount = obj.MaxTcpConnectWaitCount;
        if (obj.MaxTcpChannelCount != null) MaxTcpChannelCount = obj.MaxTcpChannelCount;
        if (obj.MaxTcpConnectsPerSecond != null) MaxTcpConnectsPerSecond = obj.MaxTcpConnectsPerSecond;
        if (obj.NetScanLimit != null) NetScanLimit = obj.NetScanLimit;
        if (obj.NetScanTimeout != null) NetScanTimeout = obj.NetScanTimeout;
        if (obj.AllowTcpForwarding != null) AllowTcpForwarding = obj.AllowTcpForwarding;
        if (obj.AllowTcpPacket != null) AllowTcpPacket = obj.AllowTcpPacket;
        if (obj.StartPadding != null) StartPadding = obj.StartPadding;
        if (obj.AllowQuicPacket != null) AllowQuicPacket = obj.AllowQuicPacket;
        if (obj.AllowUdpPacket != null) AllowUdpPacket = obj.AllowUdpPacket;
        if (obj.MinClientProtocolVersion != null) MinClientProtocolVersion = obj.MinClientProtocolVersion;
        if (obj.MaxUnauthenticatedConnectionCount != null)
            MaxUnauthenticatedConnectionCount = obj.MaxUnauthenticatedConnectionCount;
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
        MaxUdpDnsPacketsPerSecond = MaxUdpDnsPacketsPerSecondValue;
        MaxIcmpClientCount = MaxIcmpClientCountValue;
        StreamForwardingBufferSize = StreamForwardingBufferSize; //no default
        TcpKernelBufferSize = TcpKernelBufferSize; //no default
        UdpForwardingBufferSize = UdpForwardingBufferSizeValue;
        UdpChannelBufferSize = UdpChannelBufferSizeValue;
        TcpConnectTimeout = TcpConnectTimeoutValue;
        ChannelIdleTimeout = ChannelIdleTimeoutValue;
        MaxTcpConnectWaitCount = MaxTcpConnectWaitCountValue;
        MaxTcpChannelCount = MaxTcpChannelCountValue;
        MaxTcpConnectsPerSecond = MaxTcpConnectsPerSecondValue;
        NetScanLimit = NetScanLimit;
        NetScanTimeout = NetScanTimeout;
        AllowTcpForwarding = AllowTcpForwardingValue;
        AllowTcpPacket = AllowTcpPacketValue;
        AllowQuicPacket = AllowQuicPacketValue;
        AllowUdpPacket = AllowUdpPacketValue;
        MinClientProtocolVersion = MinClientProtocolVersion; //no default: the server's own minimum holds
        MaxUnauthenticatedConnectionCount = MaxUnauthenticatedConnectionCountValue;
    }
}
