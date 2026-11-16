using VpnHood.Net.Quic.Abstractions;

namespace VpnHood.Net.Quic.Ios;

/// <summary>
/// Provides the iOS QUIC client, <see cref="IosQuicClient"/>.
/// </summary>
/// <param name="memoryScale">Returns the factor the QUIC flow-control credits are multiplied by, read each time a QUIC
/// client is made: 1, or less, for the small baseline; more trades memory for throughput, as the iOS service does with 2
/// while TCP forwarding is off.</param>
public class IosQuicClientFactory(Func<int> memoryScale) : IQuicClientFactory
{
    /// <summary>
    /// Gets a value indicating whether Network.framework QUIC is supported: on iOS 15 or later.
    /// </summary>
    public bool IsSupported => IosQuicClient.IsSupported;

    /// <summary>
    /// Creates a QUIC client on Network.framework that advertises small initial flow-control credits: 4 KB per stream
    /// and 16 KB per connection, times the memory scale (at least 1). Network.framework grants more credit as it buffers
    /// data, so they bound no memory.
    /// </summary>
    /// <returns>A new <see cref="IosQuicClient"/>.</returns>
    /// <exception cref="NotSupportedException">QUIC is not supported on this platform.</exception>
    public IQuicClient CreateClient()
    {
        if (!IosQuicClient.IsSupported)
            throw new NotSupportedException("QUIC is not supported on this platform.");

        // BACKPRESSURE / NATIVE-MEMORY CAP (iOS 52 MB jetsam fix). Without an explicit QUIC receive window,
        // Network.framework advertises a large default, so on a download burst the server floods each stream
        // and inbound data buffers in NATIVE memory faster than forwarding drains it — phys_footprint spiked
        // +16 MB in ~1 s at only ~25 streams and jetsam-killed the extension. These windows are QUIC flow
        // control: the server may not send past the window until we consume and send updates.
        //   - bidi-local: per-stream window for streams WE open (TCP forwarding's download streams).
        //   - InitialMaxData: connection-wide aggregate across all streams (the real ceiling).
        // 2026-07-02: tightened 64 KB/1 MB → 32 KB/256 KB. Even with the 1 MB aggregate the downstream native
        // transients (parsed packets + NSData copies retained by NE/nw past our dispose) spiked phys_footprint
        // +6.6 MB in 250 ms at full rate and jetsam-killed the extension. 256 KB caps tunnel-wide in-flight
        // download per RTT (~20 Mbps @100 ms, ~70 Mbps @30 ms): deliberately SLOWER but with a hard memory
        // bound, per product call — stability over top speed on iOS. Pass null for any window to use the OS default.
        //
        // memoryScale lets the caller scale these baseline windows for memory/throughput trade-offs it knows
        // about (e.g. ×2 while TCP forwarding is off: TCP then rides the packet channel, so far fewer QUIC
        // streams buffer downloads concurrently). Sampled here, so the windows are fixed per QUIC client
        // (per session build); a mid-session change applies on the next reconnect.
        var scale = (ulong)Math.Max(1, memoryScale());
        return new IosQuicClient(
            initialMaxStreamDataBidirectionalLocal: scale * 4 * 1024,
            initialMaxStreamDataBidirectionalRemote: scale * 4 * 1024,
            initialMaxData: scale * 16 * 1024);
    }
}
