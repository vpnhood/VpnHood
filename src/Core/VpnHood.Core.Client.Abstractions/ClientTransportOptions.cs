using System.Text.Json.Serialization;
using VpnHood.Core.Common.Configuration;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Client.Abstractions;

/// <summary>
/// Provides the transport's settings the app tunes and the client uses, held by reference down to the session's
/// configuration in each process, so no layer restates them.
/// </summary>
/// <remarks>
/// Every setting has its own default. The two kernel-buffer settings are the exception: <see langword="null"/> there
/// leaves the socket at the OS's own size.
/// </remarks>
public class ClientTransportOptions
{
    /// <summary>
    /// Gets or sets how long a session may go without reaching its server, after an error, before it ends. The default
    /// is 3 days.
    /// </summary>
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromDays(3);

    /// <summary>
    /// Gets or sets how long the session's first request to the server may take, its connection included, before the
    /// server's own request timeout takes over; and how long a TCP connection the client opens outside the tunnel may
    /// take. The default is 30 seconds.
    /// </summary>
    public TimeSpan TcpConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long the client tries to reach its server, unstable, before it waits. The default is 60
    /// seconds.
    /// </summary>
    public TimeSpan UnstableTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Gets or sets how long the client waits before it tries again. The default is 30 seconds.</summary>
    public TimeSpan AutoWaitTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets how long a query of a server's status may take. The default is 5 seconds.</summary>
    public TimeSpan ServerQueryTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the buffer sizes of a TCP connection the client forwards. The default is about 8 KB (8,191 bytes)
    /// each way.
    /// </summary>
    // a low-memory client, as the iOS network extension under its memory limit, lowers it
    public TransferBufferSize StreamForwardingBufferSize { get; set; } = new(0xFFFF / 8, 0xFFFF / 8); // 8KB/8KB

    /// <summary>Gets or sets the buffer sizes of forwarded UDP's sockets. The default is 1 MB each way.</summary>
    public TransferBufferSize UdpForwardingBufferSize { get; set; } = new(1024 * 1024, 1024 * 1024);

    /// <summary>
    /// Gets or sets the kernel buffer sizes of the UDP socket of the UDP channel to the server. The default is 1 MB each
    /// way.
    /// </summary>
    public TransferBufferSize UdpChannelBufferSize { get; set; } = new(1024 * 1024, 1024 * 1024);

    /// <summary>
    /// Gets or sets the buffer sizes of a packet channel's connection. The default is about 256 KB each way.
    /// </summary>
    public TransferBufferSize PacketChannelBufferSize { get; set; } = TransportDefaults.ConnectionPacketBufferSize;

    /// <summary>
    /// Gets or sets the most packet channels the client keeps over TCP or QUIC, which the server may lower. The default
    /// is 8.
    /// </summary>
    /// <remarks>
    /// Over TCP, each is a whole connection, with its TLS and buffers, so the count trades memory for throughput: packet
    /// mode carries every tunneled flow over these, and upload spreads across them by source port. Over QUIC, each is a
    /// stream, and streams may share a connection. UDP has one channel.
    /// </remarks>
    public int MaxPacketChannelCount { get; set; } = TransportDefaults.MaxPacketChannelCount;

    /// <summary>
    /// Gets or sets the kernel buffer sizes of the client's TCP sockets, but those of the packet channels it connects
    /// directly, or <see langword="null"/> for the OS's own.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TransferBufferSize? TcpKernelBufferSize { get; set; }

    /// <summary>
    /// Gets or sets the kernel buffer sizes of the TCP packet channels to the server, or <see langword="null"/> for the
    /// OS's own, not <see cref="TcpKernelBufferSize"/>'s, so the tunnel's sockets can autotune while the others stay
    /// small. A packet channel through a proxy keeps <see cref="TcpKernelBufferSize"/>'s, as the proxy makes its socket.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TransferBufferSize? TcpPacketChannelKernelBufferSize { get; set; }

    /// <summary>Gets or sets the most of a session's UDP forwarding workers. The default is 100.</summary>
    public int MaxUdpClientCount { get; set; } = TransportDefaults.MaxUdpClientCount;

    /// <summary>Gets or sets the most of a session's DNS forwarding workers. The default is 70.</summary>
    public int MaxUdpDnsClientCount { get; set; } = TransportDefaults.MaxUdpDnsClientCount;

    /// <summary>Gets or sets the packets a UDP forwarder's queue holds. The default is 200.</summary>
    public int UdpForwardingQueueCapacity { get; set; } = TransportDefaults.PacketForwardingQueueCapacity;

    /// <summary>
    /// Gets a new instance of the ordinary transport, every setting at its own default: what every host runs unless
    /// something constrains it — Windows, Linux, macOS, Android, and an iOS app running on an Apple Silicon Mac.
    /// </summary>
    /// <remarks>
    /// It is named for asking nothing unusual rather than for wanting a lot of memory: <see cref="LimitedMemory"/> is
    /// the exception, this is the norm.
    /// </remarks>
    public static ClientTransportOptions NormalMemory => new();

    /// <summary>
    /// Gets a new instance of the constrained preset, for a host that runs network code inside a memory-capped process
    /// — today the iOS/tvOS Network Extension under its ~52 MB jetsam limit.
    /// </summary>
    /// <remarks>
    /// Every value was measured against that cap. Nothing else needs it: the limit is the platform's, not a property of
    /// the device, which is why an Apple Silicon Mac running the same iOS binary takes <see cref="NormalMemory"/>
    /// instead.
    /// </remarks>
    public static ClientTransportOptions LimitedMemory => new() {
        // Shrink the transport coalescing buffers to keep memory usage low; these flow to the
        // extension via vpn.config. Desktop keeps the 256 KB default. 64 KB holds ~45 MTU packets
        // with negligible throughput impact below ~200 Mbps.
        PacketChannelBufferSize = new TransferBufferSize(16 * 1024, 16 * 1024),
        UdpForwardingBufferSize = new TransferBufferSize(16 * 1024, 16 * 1024),
        // A post-kill reconnect opens one direct UdpClient per excluded UDP flow (exclude-country
        // sends carrier DNS + in-country UDP outside the tunnel): the 2026-07-17 capture died in
        // ~20 s at 222 forwarders × 200-packet queues, all managed memory. The desktop-scale defaults
        // (100 × 200) never engage before jetsam; bound the fleet and the per-forwarder queue instead.
        MaxUdpClientCount = 50,
        // DNS workers are segregated, tiny (4 KB) and recycle every UdpDnsTimeout (10 s), so this
        // bounds a DNS storm without letting it starve the general pool above.
        MaxUdpDnsClientCount = 100,
        UdpForwardingQueueCapacity = 16,
        // Two, not the normal maximum: each channel is a TLS connection held for the session's life,
        // and the extension pays for all of them inside the ~52 MB cap. Two is where the measured
        // curve flattens — download rides a single channel regardless (the server picks by source
        // port, which is 443 for nearly all download traffic), so extra channels only spread upload,
        // and the 2026-08-30 device runs reached the link ceiling on upload with the tunnel sockets
        // left to autotune. Judge any increase against ext-mem.log, not throughput alone.
        MaxPacketChannelCount = 2,
        // UPLOAD/DOWNLOAD SPEED: the forwarding copy pump is a serial read→write→flush loop, so per-flow
        // throughput ≈ StreamForwardingBufferSize / RTT. 2 KB capped it at ~2 Mbps. 32 KB lifts that ~16×.
        // (Memory is per-ACTIVE-flow: 2 buffers × 32 KB; the many-idle-flows case is bounded separately.)
        StreamForwardingBufferSize = new TransferBufferSize(32 * 1024, 32 * 1024),
        // Kernel buffer for EVERY managed TCP socket the client opens — the transport connections
        // to the server AND the per-flow direct sockets of split/exclude ("passthru") flows. The
        // passthru sockets are the sizing constraint: unlike tunneled flows (QUIC streams bounded
        // tunnel-wide by IosQuicClient's 256 KB aggregate window), each excluded flow pins its own
        // socket buffers, up to the TcpStack's 40-connection cap. The former 256 KB let a
        // split-country browse pin ~40 × 512 KB ≈ 20 MB and jetsam the extension; 64 KB bounds the
        // worst case to ~5 MB while still allowing ~25 Mbps per flow at 20 ms RTT (in-country hosts
        // are low-RTT).
        TcpKernelBufferSize = new TransferBufferSize(64 * 1024, 64 * 1024),
        // The one knob this preset deliberately does NOT shrink. Packet mode multiplexes every inner
        // TCP flow over one outer TCP connection, so its throughput is window/RTT for the whole
        // tunnel, and any explicit SO_SNDBUF/SO_RCVBUF disables Darwin's autotune (which grows to
        // ~2 MB under load). Measured on-device at 75 ms RTT (2026-08-30): pinned at 256 KB gave
        // 26 Mbps download and ~60 Mbps upload; left alone it reached 210 down / ~100 up, the latter
        // being the link ceiling. Android pins nothing and was always fast. Kernel socket memory is
        // charged once for the tunnel, not per flow, so autotune costs far less here than the
        // 40-connection passthru case that forced TcpKernelBufferSize down to 64 KB.
        TcpPacketChannelKernelBufferSize = null
    };

    /// <summary>
    /// Picks the preset that fits the current host. Apple mobile platforms (iOS / tvOS) run network
    /// code inside memory-capped Network Extensions, so they get <see cref="LimitedMemory"/>;
    /// everything else gets <see cref="NormalMemory"/> so throughput is unaffected. Detection is by OS family,
    /// not a runtime memory probe: an extension's real limit is enforced by jetsam and is not
    /// visible through <see cref="GC.GetGCMemoryInfo()"/>, which reports total device RAM.
    /// <para>
    /// This is what <c>AppOptions.Transport</c> starts as, so a head gets the right preset without
    /// writing anything. One case it cannot see: an iOS app running on Apple Silicon under
    /// "Designed for iPad" reports <c>IsIOS()</c> with no Mac Catalyst marker, so it is
    /// indistinguishable from a real device here. Only a head can tell, by asking Foundation
    /// (<c>NSProcessInfo.ProcessInfo.IsiOSApplicationOnMac</c>), and it assigns the preset itself.
    /// </para>
    /// </summary>
    /// <returns>
    /// A new <see cref="LimitedMemory"/> preset on iOS and tvOS; otherwise, a new <see cref="NormalMemory"/> one.
    /// </returns>
    public static ClientTransportOptions ForCurrentPlatform()
    {
        // Apple mobile runs its network code inside a memory-capped Network Extension.
        var isAppleMobile = OperatingSystem.IsIOS() || OperatingSystem.IsTvOS();
        return isAppleMobile ? LimitedMemory : NormalMemory;
    }
}
