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

    // Null on these two means "leave the socket at the system default" — a real value, not "unset".
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TransferBufferSize? TcpKernelBufferSize { get; set; }

    // Optional kernel buffer used only by TCP connections to the VPN server. This lets
    // memory-constrained clients keep direct/split-flow sockets small without throttling the
    // packet channel, where one outer TCP connection carries all tunneled flows.
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
        // Packet mode multiplexes every inner TCP flow over one outer TCP connection on iOS.
        // A 64 KB outer socket window caps that entire tunnel near 10-15 Mbps at common WAN RTTs.
        // Give only the server tunnel socket a 256 KB BDP window; direct/split-flow sockets retain
        // the 64 KB cap above, preserving the per-flow jetsam memory bound.
        TcpPacketChannelKernelBufferSize = new TransferBufferSize(256 * 1024, 256 * 1024)
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
