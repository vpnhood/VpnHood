using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Server.Access.Configurations;

/// <summary>
/// Represents the limits, timeouts and buffers of the server's sessions.
/// </summary>
/// <remarks>
/// A setting left <see langword="null"/> takes its default, which the property of its name ending in <c>Value</c>
/// gives; a few have no default here and leave the server's own.
/// </remarks>
public class SessionOptions
{
    /// <summary>
    /// Gets or sets how long a session may go without activity before the server closes it, or <see langword="null"/>
    /// for 30 minutes.
    /// </summary>
    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets or sets how long a session's UDP worker, where the server forwards UDP itself, may stay idle, or
    /// <see langword="null"/> for 1 minute; DNS's workers have their own, shorter timeout.
    /// </summary>
    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? UdpTimeout { get; set; }

    /// <summary>
    /// Gets or sets a TCP idle timeout, or <see langword="null"/> for 15 minutes, which nothing in the server reads now.
    /// </summary>
    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? TcpTimeout { get; set; }

    /// <summary>
    /// Gets or sets how long a destination a session pings stays tracked, where the server forwards ICMP itself, or
    /// <see langword="null"/> for 30 seconds; the ICMP workers themselves last 5 minutes idle.
    /// </summary>
    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? IcmpTimeout { get; set; }

    /// <summary>
    /// Gets or sets how long after its last report a session's traffic is due again, which the server then reports at
    /// its next collection, or <see langword="null"/> for 20 minutes; it caps the status interval too.
    /// </summary>
    /// <remarks>The access manager times out a live session it hears nothing of.</remarks>
    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? SyncInterval { get; set; }

    /// <summary>
    /// Gets or sets the unreported traffic, in bytes, at which a session's traffic is due at once, reported at the
    /// server's next collection, or <see langword="null"/> for 100 MB (100,000,000 bytes).
    /// </summary>
    public long? SyncCacheSize { get; set; }

    /// <summary>
    /// Gets or sets the most active TCP or QUIC packet channels a session may keep, which the server tells the client, or
    /// <see langword="null"/> for 8; the tunnel's UDP channel is apart.
    /// </summary>
    public int? MaxPacketChannelCount { get; set; }

    /// <summary>
    /// Gets or sets the most UDP workers a session may hold where the server forwards UDP itself, or
    /// <see langword="null"/> for 500; DNS's workers have their own quota.
    /// </summary>
    public int? MaxUdpClientCount { get; set; }

    /// <summary>
    /// Gets or sets the most DNS packets over UDP a session may send a second, or <see langword="null"/> for 200.
    /// </summary>
    public int? MaxUdpDnsPacketsPerSecond { get; set; }

    /// <summary>
    /// Gets or sets the most ICMP workers a session may hold where the server forwards ICMP itself, or
    /// <see langword="null"/> for 20.
    /// </summary>
    public int? MaxIcmpClientCount { get; set; }

    /// <summary>
    /// Gets or sets the kernel buffer sizes of the server's TCP sockets, or <see langword="null"/> for the server's
    /// default, which is the OS's own unless the host sets one.
    /// </summary>
    public TransferBufferSize? TcpKernelBufferSize { get; set; }

    /// <summary>
    /// Gets or sets the buffer sizes of a TCP connection the server forwards, or <see langword="null"/> for the
    /// server's choice: about 8 KB each way.
    /// </summary>
    public TransferBufferSize? StreamForwardingBufferSize { get; set; }

    /// <summary>
    /// Gets or sets the buffer sizes of the UDP forwarders' sockets, or <see langword="null"/> for the server's own;
    /// <see cref="UdpChannelBufferSize"/> is the tunnel's own UDP channel's.
    /// </summary>
    public TransferBufferSize? UdpForwardingBufferSize { get; set; }

    /// <summary>
    /// Gets or sets the kernel buffer sizes of the socket of the tunnel's own UDP channel, or <see langword="null"/>
    /// for the server's own.
    /// </summary>
    public TransferBufferSize? UdpChannelBufferSize { get; set; }

    /// <summary>
    /// Gets or sets how long a request on a new client connection may take, the server's connect to its destination
    /// included, and how long the server tries to answer or close a failed one, or <see langword="null"/> for 30
    /// seconds; a client waits a little longer for the server's answer.
    /// </summary>
    /// <remarks>A request on a kept connection has <see cref="ChannelIdleTimeout"/> instead.</remarks>
    public TimeSpan? TcpConnectTimeout { get; set; }

    /// <summary>
    /// Gets or sets how long the server keeps a client's idle connection open for its next request, which then has
    /// that long again, or <see langword="null"/> for 40 seconds; the client is told a little less.
    /// </summary>
    public TimeSpan? ChannelIdleTimeout { get; set; }

    /// <summary>
    /// Gets or sets the most TCP connects a session may have under way at once, or <see langword="null"/> for 500.
    /// </summary>
    public int? MaxTcpConnectWaitCount { get; set; }

    /// <summary>
    /// Gets or sets the count of a session's forwarded TCP connections, packet channels and connects under way together
    /// at which the server refuses another forwarded TCP connection, or <see langword="null"/> for 1,000.
    /// </summary>
    public int? MaxTcpChannelCount { get; set; }

    /// <summary>
    /// Gets or sets the most new TCP connections a session may make a second, or <see langword="null"/> for 200.
    /// </summary>
    public int? MaxTcpConnectsPerSecond { get; set; }

    /// <summary>
    /// Gets or sets the most endpoints of one IPv4 /24 network a session may reach within
    /// <see cref="NetScanTimeout"/>, or <see langword="null"/>, as that one may be, for no limit. It counts the
    /// session's TCP connections whichever way they go, and its UDP and ICMP where the server forwards them itself.
    /// </summary>
    public int? NetScanLimit { get; set; }

    /// <summary>
    /// Gets or sets how long an endpoint counts toward <see cref="NetScanLimit"/>, or <see langword="null"/>, as that
    /// one may be, for no limit.
    /// </summary>
    public TimeSpan? NetScanTimeout { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether TCP may go by forwarding: the client ends each TCP connection on the
    /// device and the server opens a new one, passing the bytes between them; or <see langword="null"/> for
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Turning one way off leaves TCP going by the other (<see cref="AllowTcpPacket"/>); both off leave it no way
    /// through, which the server refuses.
    /// </remarks>
    public bool? AllowTcpForwarding { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether TCP may go as raw IP packets in the tunnel, running end to end; or
    /// <see langword="null"/> for <see langword="true"/>.
    /// </summary>
    public bool? AllowTcpPacket { get; set; }

    /// <summary>
    /// Gets or sets <see cref="AllowTcpForwarding"/> under its former name, which server profiles, config files and
    /// older servers hold; a value under the new name wins.
    /// </summary>
    [Obsolete("Use AllowTcpForwarding.")]
    public bool? AllowTcpProxy {
        get => AllowTcpForwarding;
        set => AllowTcpForwarding ??= value;
    }

    /// <summary>
    /// Gets or sets <see cref="StreamForwardingBufferSize"/> under its former name; a value under the new name wins.
    /// </summary>
    [Obsolete("Use StreamForwardingBufferSize.")]
    public TransferBufferSize? StreamProxyBufferSize {
        get => StreamForwardingBufferSize;
        set => StreamForwardingBufferSize ??= value;
    }

    /// <summary>
    /// Gets or sets <see cref="UdpForwardingBufferSize"/> under its former name; a value under the new name wins.
    /// </summary>
    [Obsolete("Use UdpForwardingBufferSize.")]
    public TransferBufferSize? UdpProxyBufferSize {
        get => UdpForwardingBufferSize;
        set => UdpForwardingBufferSize ??= value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the server adds cover traffic at the start of WebSocket forwarding
    /// streams, never of packet channels: <see langword="null"/> inherits a layered configuration, and an explicit
    /// <see langword="false"/> overrides <see langword="true"/>. The effective default is off.
    /// </summary>
    public bool? StartPadding { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the users' own QUIC may go in the tunnel, not the server's QUIC channel,
    /// or <see langword="null"/> for <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Turned off, the server drops the users' UDP to ports 80 and 443, but DNS's; it is enforced only with
    /// <see cref="AllowTcpPacket"/> off, as otherwise the tunnel carries the users' TCP as packets anyway.
    /// </remarks>
    public bool? AllowQuicPacket { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the users' own UDP may go in the tunnel, not the server's UDP channel,
    /// or <see langword="null"/> for <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Turned off, the server drops the users' UDP, but DNS's; it is enforced only with <see cref="AllowTcpPacket"/>
    /// off, as for <see cref="AllowQuicPacket"/>.
    /// </remarks>
    public bool? AllowUdpPacket { get; set; }

    /// <summary>
    /// Gets or sets the oldest client protocol the server takes, or <see langword="null"/> for the server's own minimum;
    /// a lower value cannot lower that.
    /// </summary>
    /// <remarks>
    /// At 15 or more, it is checked before the server reads a body or upgrades the connection. Below 15, a legacy
    /// client's hello is read, and a client older than the minimum is refused with an update message.
    /// </remarks>
    public int? MinClientProtocolVersion { get; set; }

    /// <summary>
    /// Gets or sets the most of the server's connections that have not proven a key yet, its port 80's included, or
    /// <see langword="null"/> for 1,000; above it, the address that holds the most loses its oldest.
    /// </summary>
    public int? MaxUnauthenticatedConnectionCount { get; set; }

    /// <summary>Gets <see cref="Timeout"/>, or its default, 30 minutes.</summary>
    [JsonIgnore]
    public TimeSpan TimeoutValue => Timeout ?? TimeSpan.FromMinutes(30);

    /// <summary>Gets <see cref="UdpTimeout"/>, or its default, 1 minute.</summary>
    [JsonIgnore]
    public TimeSpan UdpTimeoutValue => UdpTimeout ?? TimeSpan.FromMinutes(1);

    /// <summary>Gets <see cref="UdpChannelBufferSize"/>, which has no default here.</summary>
    [JsonIgnore]
    public TransferBufferSize? UdpChannelBufferSizeValue => UdpChannelBufferSize ?? null;

    /// <summary>Gets <see cref="UdpForwardingBufferSize"/>, which has no default here.</summary>
    [JsonIgnore]
    public TransferBufferSize? UdpForwardingBufferSizeValue => UdpForwardingBufferSize ?? null;

    /// <summary>Gets <see cref="TcpTimeout"/>, or its default, 15 minutes.</summary>
    [JsonIgnore]
    public TimeSpan TcpTimeoutValue => TcpTimeout ?? TimeSpan.FromMinutes(15);

    /// <summary>Gets <see cref="IcmpTimeout"/>, or its default, 30 seconds.</summary>
    [JsonIgnore]
    public TimeSpan IcmpTimeoutValue => IcmpTimeout ?? TimeSpan.FromSeconds(30);

    /// <summary>Gets <see cref="SyncInterval"/>, or its default, 20 minutes.</summary>
    [JsonIgnore]
    public TimeSpan SyncIntervalValue => SyncInterval ?? TimeSpan.FromMinutes(20);

    /// <summary>Gets <see cref="SyncCacheSize"/>, or its default, 100 MB (100,000,000 bytes).</summary>
    [JsonIgnore]
    public long SyncCacheSizeValue => SyncCacheSize ?? 100 * 1000000;

    /// <summary>Gets <see cref="MaxPacketChannelCount"/>, or its default, 8.</summary>
    [JsonIgnore]
    public int MaxPacketChannelCountValue => MaxPacketChannelCount ?? 8;

    /// <summary>Gets <see cref="MaxUdpClientCount"/>, or its default, 500.</summary>
    [JsonIgnore]
    public int MaxUdpClientCountValue => MaxUdpClientCount ?? 500;

    /// <summary>
    /// Gets <see cref="MaxUdpDnsPacketsPerSecond"/>, or its default, 200: meant to be far above a device's own lookups
    /// and far below a flood that would get the server reported.
    /// </summary>
    [JsonIgnore]
    public int MaxUdpDnsPacketsPerSecondValue => MaxUdpDnsPacketsPerSecond ?? 200;

    /// <summary>Gets <see cref="MaxIcmpClientCount"/>, or its default, 20.</summary>
    [JsonIgnore]
    public int MaxIcmpClientCountValue => MaxIcmpClientCount ?? 20;

    /// <summary>Gets <see cref="TcpConnectTimeout"/>, or its default, 30 seconds.</summary>
    [JsonIgnore]
    public TimeSpan TcpConnectTimeoutValue => TcpConnectTimeout ?? TimeSpan.FromSeconds(30);

    /// <summary>Gets <see cref="ChannelIdleTimeout"/>, or its default, 40 seconds.</summary>
    [JsonIgnore]
    public TimeSpan ChannelIdleTimeoutValue => ChannelIdleTimeout ?? TimeSpan.FromSeconds(40);

    /// <summary>Gets <see cref="MaxTcpConnectWaitCount"/>, or its default, 500.</summary>
    [JsonIgnore]
    public int MaxTcpConnectWaitCountValue => MaxTcpConnectWaitCount ?? 500;

    /// <summary>Gets <see cref="MaxTcpChannelCount"/>, or its default, 1,000.</summary>
    [JsonIgnore]
    public int MaxTcpChannelCountValue => MaxTcpChannelCount ?? 1000;

    /// <summary>
    /// Gets <see cref="MaxTcpConnectsPerSecond"/>, or its default, 200: meant to be far above a browser's bursts and far
    /// below a SYN flood.
    /// </summary>
    [JsonIgnore]
    public int MaxTcpConnectsPerSecondValue => MaxTcpConnectsPerSecond ?? 200;

    /// <summary>
    /// Gets <see cref="MaxUnauthenticatedConnectionCount"/>, or its default, 1,000: an estimate, on the reasoning that a
    /// client's connection proves its key within about a second, so the count stays near the server's new connections a
    /// second, and a cap well above that leaves a mass reconnect alone.
    /// </summary>
    [JsonIgnore]
    public int MaxUnauthenticatedConnectionCountValue => MaxUnauthenticatedConnectionCount ?? 1000;

    /// <summary>Gets <see cref="AllowTcpForwarding"/>, or its default, <see langword="true"/>.</summary>
    [JsonIgnore]
    public bool AllowTcpForwardingValue => AllowTcpForwarding ?? true;

    /// <summary>Gets <see cref="StartPadding"/>, or its default, <see langword="false"/>.</summary>
    [JsonIgnore]
    public bool StartPaddingValue => StartPadding ?? false;

    /// <summary>Gets <see cref="AllowTcpPacket"/>, or its default, <see langword="true"/>.</summary>
    [JsonIgnore]
    public bool AllowTcpPacketValue => AllowTcpPacket ?? true;

    /// <summary>Gets <see cref="AllowQuicPacket"/>, or its default, <see langword="true"/>.</summary>
    [JsonIgnore]
    public bool AllowQuicPacketValue => AllowQuicPacket ?? true;

    /// <summary>Gets <see cref="AllowUdpPacket"/>, or its default, <see langword="true"/>.</summary>
    [JsonIgnore]
    public bool AllowUdpPacketValue => AllowUdpPacket ?? true;

    /// <summary>Copies over these options each one another set sets.</summary>
    /// <param name="obj">The options that win where they are set.</param>
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

    /// <summary>
    /// Sets each option left <see langword="null"/> to its default; those with none here — the TCP buffers, the net scan
    /// limits and the client protocol — and the start padding, left to inherit, stay as they are.
    /// </summary>
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
