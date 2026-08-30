using System.Text.Json.Serialization;
using VpnHood.Core.Common.Configuration;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Client.Abstractions;

/// <summary>
/// The transport knobs the app tunes and the client forwards, held by reference from
/// <c>AppOptions</c> all the way down to the session config so no layer restates them.
/// <para>
/// Every knob carries its own default, so the value and the knob live in one place and no consumer
/// has to resolve anything on the way through. The two kernel-buffer knobs are the exception: null
/// there is a real value meaning "leave the socket at the operating system's own size".
/// </para>
/// </summary>
public class ClientTransportOptions
{
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromDays(3);
    public TimeSpan TcpConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan UnstableTimeout { get; set; } = TimeSpan.FromSeconds(60); // connect timeout before pause
    public TimeSpan AutoWaitTimeout { get; set; } = TimeSpan.FromSeconds(30); // auto resume after pause
    public TimeSpan ServerQueryTimeout { get; set; } = TimeSpan.FromSeconds(5);

    // Per-platform transport buffer sizes. Low-memory clients (e.g. the iOS Network Extension
    // under the ~50 MB jetsam limit) lower these. The three below are the client's alone, so they
    // carry their own default rather than deferring to a shared constant.
    public TransferBufferSize StreamProxyBufferSize { get; set; } = new(0xFFFF / 8, 0xFFFF / 8); // 8KB/8KB
    public TransferBufferSize UdpProxyBufferSize { get; set; } = new(1024 * 1024, 1024 * 1024);

    // Kernel buffer of the UDP socket carrying the udp channel to the server.
    public TransferBufferSize UdpChannelBufferSize { get; set; } = new(1024 * 1024, 1024 * 1024);

    public TransferBufferSize PacketChannelBufferSize { get; set; } = TransportDefaults.ConnectionPacketBufferSize;

    // How many packet channels the client asks the server for (the server caps it in turn, see
    // ClientSessionBuilder). Each is a full transport connection — socket, TLS state and a coalescing
    // buffer pair — so it is a throughput/memory trade, which is why it belongs to the preset rather
    // than to the user: TCP packet mode multiplexes every tunneled flow over these, and upload spreads
    // across them by source port. Applies to TCP only; UDP is single-channel by design (ClientSession).
    public int MaxPacketChannelCount { get; set; } = TransportDefaults.MaxPacketChannelCount;

    // Null on these two means "leave the socket at the system default" — a real value, not "unset".
    // Kernel buffer for every managed TCP socket except the packet channels below.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TransferBufferSize? TcpKernelBufferSize { get; set; }

    // Kernel buffer for the TCP packet channels to the VPN server, which each carry every tunneled
    // flow multiplexed together and so size independently of the per-flow sockets above. Null does
    // NOT inherit TcpKernelBufferSize: it means the OS default, so a memory-constrained client can
    // hold the per-flow sockets small while leaving the tunnel free to autotune.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TransferBufferSize? TcpPacketChannelKernelBufferSize { get; set; }

    // Per-platform UDP proxy scaling. Low-memory clients cap the direct-UDP socket fleet and the
    // per-proxy packet queue so a post-kill reconnect flow-storm stays bounded.
    public int MaxUdpClientCount { get; set; } = TransportDefaults.MaxUdpClientCount;
    public int MaxUdpDnsClientCount { get; set; } = TransportDefaults.MaxUdpDnsClientCount;
    public int UdpProxyQueueCapacity { get; set; } = TransportDefaults.ProxyPacketQueueCapacity;

    /// <summary>
    /// The ordinary transport: every knob left null so each core component applies its own default.
    /// What every host runs unless something constrains it — Windows, Linux, macOS, Android, and an
    /// iOS app running on an Apple Silicon Mac. It is named for asking nothing unusual rather than
    /// for wanting a lot of memory: <see cref="LimitedMemory"/> is the exception, this is the norm.
    /// </summary>
    public static ClientTransportOptions NormalMemory => new();

    /// <summary>
    /// The constrained preset for a host that runs network code inside a memory-capped process —
    /// today the iOS/tvOS Network Extension under its ~52 MB jetsam limit. Every value here was
    /// measured against that cap; see the per-knob notes. Nothing else needs it: the limit is the
    /// platform's, not a property of the device, which is why an Apple Silicon Mac running the same
    /// iOS binary takes <see cref="NormalMemory"/> instead.
    /// </summary>
    public static ClientTransportOptions LimitedMemory => new() {
        // Shrink the transport coalescing buffers to keep memory usage low; these flow to the
        // extension via vpn.config. Desktop keeps the 256 KB default. 64 KB holds ~45 MTU packets
        // with negligible throughput impact below ~200 Mbps.
        PacketChannelBufferSize = new TransferBufferSize(16 * 1024, 16 * 1024),
        UdpProxyBufferSize = new TransferBufferSize(16 * 1024, 16 * 1024),
        // A post-kill reconnect opens one direct UdpClient per excluded UDP flow (exclude-country
        // sends carrier DNS + in-country UDP outside the tunnel): the 2026-07-17 capture died in
        // ~20 s at 222 proxies × 200-packet queues, all managed memory. The desktop-scale defaults
        // (100 × 200) never engage before jetsam; bound the fleet and the per-proxy queue instead.
        MaxUdpClientCount = 50,
        // DNS workers are segregated, tiny (4 KB) and recycle every UdpDnsTimeout (10 s), so this
        // bounds a DNS storm without letting it starve the general pool above.
        MaxUdpDnsClientCount = 100,
        UdpProxyQueueCapacity = 16,
        // Two, not the normal maximum: each channel is a TLS connection held for the session's life,
        // and the extension pays for all of them inside the ~52 MB cap. Two is where the measured
        // curve flattens — download rides a single channel regardless (the server picks by source
        // port, which is 443 for nearly all download traffic), so extra channels only spread upload,
        // and the 2026-08-30 device runs reached the link ceiling on upload with the tunnel sockets
        // left to autotune. Judge any increase against ext-mem.log, not throughput alone.
        MaxPacketChannelCount = 2,
        // UPLOAD/DOWNLOAD SPEED: the proxy copy pump is a serial read→write→flush loop, so per-flow
        // throughput ≈ StreamProxyBufferSize / RTT. 2 KB capped it at ~2 Mbps. 32 KB lifts that ~16×.
        // (Memory is per-ACTIVE-flow: 2 buffers × 32 KB; the many-idle-flows case is bounded separately.)
        StreamProxyBufferSize = new TransferBufferSize(32 * 1024, 32 * 1024),
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
    public static ClientTransportOptions ForCurrentPlatform()
    {
        // Apple mobile runs its network code inside a memory-capped Network Extension.
        var isAppleMobile = OperatingSystem.IsIOS() || OperatingSystem.IsTvOS();
        return isAppleMobile ? LimitedMemory : NormalMemory;
    }
}
