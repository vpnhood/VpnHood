using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Toolkit.Sockets;

namespace VpnHood.Core.Quic.Ios;

/// <summary>
/// A <see cref="SystemSocketFactory"/> that adds iOS (Network.framework) QUIC client support.
/// TCP/UDP creation is inherited from the base factory.
/// </summary>
/// <param name="memoryScale">Scale factor applied to the QUIC receive windows; sampled each time a QUIC
/// client is created. 1 = the tight jetsam-safe baseline; larger values trade memory for throughput
/// (e.g. the iOS service passes 2 while the TCP proxy is off — see <see cref="CreateQuicClient"/>).</param>
public class IosSocketFactory(Func<int> memoryScale) : SystemSocketFactory
{
    public override bool IsQuicSupported => IosQuicClient.IsSupported;

    public override IQuicClient CreateQuicClient()
    {
        if (!IosQuicClient.IsSupported)
            throw new NotSupportedException("QUIC is not supported on this platform.");

        // BACKPRESSURE / NATIVE-MEMORY CAP (iOS 52 MB jetsam fix). Without an explicit QUIC receive window,
        // Network.framework advertises a large default, so on a download burst the server floods each stream
        // and inbound data buffers in NATIVE memory faster than the proxy drains it — phys_footprint spiked
        // +16 MB in ~1 s at only ~25 streams and jetsam-killed the extension. These windows are QUIC flow
        // control: the server may not send past the window until we consume and send updates.
        //   - bidi-local: per-stream window for streams WE open (the proxy's download streams).
        //   - InitialMaxData: connection-wide aggregate across all streams (the real ceiling).
        // 2026-07-02: tightened 64 KB/1 MB → 32 KB/256 KB. Even with the 1 MB aggregate the downstream native
        // transients (parsed packets + NSData copies retained by NE/nw past our dispose) spiked phys_footprint
        // +6.6 MB in 250 ms at full rate and jetsam-killed the extension. 256 KB caps tunnel-wide in-flight
        // download per RTT (~20 Mbps @100 ms, ~70 Mbps @30 ms): deliberately SLOWER but with a hard memory
        // bound, per product call — stability over top speed on iOS. Pass null for any window to use the OS default.
        //
        // memoryScale lets the caller scale these baseline windows for memory/throughput trade-offs it knows
        // about (e.g. ×2 while the TCP proxy is off: TCP then rides the packet channel, so far fewer QUIC
        // streams buffer downloads concurrently). Sampled here, so the windows are fixed per QUIC client
        // (per session build); a mid-session change applies on the next reconnect.
        var scale = (ulong)Math.Max(1, memoryScale());
        return new IosQuicClient(
            initialMaxStreamDataBidirectionalLocal: scale * 4 * 1024,
            initialMaxStreamDataBidirectionalRemote: scale * 4 * 1024,
            initialMaxData: scale * 16 * 1024);
    }
}
