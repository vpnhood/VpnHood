using System.Threading.Channels;
using CoreFoundation;
using Microsoft.Extensions.Logging;
using Network;
using ObjCRuntime;
using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Quic.Ios;

/// <summary>
/// Client-side QUIC connector for iOS, backed by Apple's Network.framework (iOS 15+). Establishes a
/// multiplexed QUIC tunnel and exposes lightweight streams over it, matching the desktop MsQuic client.
/// </summary>
public sealed class IosQuicClient : IQuicClient
{
    public static bool IsSupported => OperatingSystem.IsIOSVersionAtLeast(15);

    // ToDo: remove diagnose
    // DIAGNOSTIC: live count of open QUIC streams (= native NWConnections). Maintained by IosQuicStream;
    // exposed here (public) so the memory probe in another assembly can show whether streams are released
    // promptly at flow-end (drops) or linger (stays high) on iOS.
    public static int LiveStreamCount;

    // DIAGNOSTIC: monotonic id assigned to each QUIC stream so open/close lines can be paired in the log.
    // Stream open/close events are logged directly via VhLogger.Instance in IosQuicStream; with LogToFile
    // they land in the pullable log file and with LogToDevice they also reach the iOS unified log
    // (Console.app) through the injected IosDeviceLoggerProvider (os_log).
    public static int StreamSeq;

    // DIAGNOSTIC: total bytes handed to nw_connection_send whose completion callback has NOT yet fired,
    // summed across all live streams. Maintained by IosQuicStream; read by the memory probe (sendQ=).
    // Discriminates the 2026-07-01 jetsam-during-upload native balloon: if this tracks the native climb,
    // Network.framework is delaying completions (real backpressure, our aggregate cap is missing); if it
    // stays ~0 while native climbs, completions fire on-enqueue and the balloon is nw-internal buffering.
    // (2026-07-01 run 2 verdict: stays ~0 — completions are prompt; the balloon is elsewhere.)
    public static long OutstandingSendBytes;

    // DIAGNOSTIC: worst single IosQuicStream teardown (NWConnection.Cancel + Dispose) duration since the
    // probe last reset it. The 2026-07-01 whole-tunnel freezes start right after connection-teardown
    // BURSTS (conn 29->22, 14->7 one tick before each freeze); if Cancel/Dispose blocks on the shared
    // nw dispatch queue, a teardown burst would stall every flow on the tunnel — this measures that.
    public static long MaxStreamCancelMs;

    // JETSAM GUARD input: the extension's live phys_footprint in MB, written ~4x/s by the host's memory
    // probe (IosVpnService). At full download rate (~130 Mbps) the per-packet native transients
    // (NSData copies retained briefly by NE/nw) float several MB and their peaks ratchet over the 52 MB
    // limit with no leak, freeze, or backlog anywhere we can bound directly (2026-07-01 third crash
    // flavor). IosQuicStream.ReadAsync brakes download intake while this is within a few MB of the
    // limit, letting the transients drain. 0 = unknown/probe not running -> guard inactive.
    public static double FootprintMb;

    public async ValueTask<IQuicConnection> ConnectAsync(
        QuicClientConnectOptions options, CancellationToken cancellationToken)
    {
        var endpoint = NWEndpoint.Create(
            options.RemoteEndPoint.Address.ToString(),
            options.RemoteEndPoint.Port.ToString())
            ?? throw new IOException("Failed to create a QUIC endpoint.");
        var queue = new DispatchQueue("VpnHood.Quic.Ios");
        var multiplexGroup = new NWMultiplexGroup(endpoint);

        // QUIC parameters: ALPN "h3" (the desktop uses SslApplicationProtocol.Http3 purely as the ALPN
        // token — our QUIC is a custom transport, not HTTP/3) and the pinned-certificate verify bridge.
        var parameters = NWParameters.CreateQuic(quicOptions => {
            // The .NET Network binding types this callback's argument as the base NWProtocolOptions even
            // though the underlying native object is a QUIC options block. A direct C# cast to
            // NWProtocolQuicOptions throws InvalidCastException (the managed wrapper is literally
            // NWProtocolOptions) — which, thrown on this native trampoline thread, SIGABRTs the process.
            // Re-wrap the SAME native handle as NWProtocolQuicOptions (owns:false — the parameters own
            // the native object); setters then mutate the real options block used by the connection.
            var quic = Runtime.GetINativeObject<NWProtocolQuicOptions>(quicOptions.Handle, owns: false)
                ?? throw new IOException("Failed to access QUIC protocol options.");
            quic.AddTlsApplicationProtocol("h3");
            quic.InitialMaxStreamsBidirectional = (ulong)options.MaxInboundBidirectionalStreams;

            // BACKPRESSURE / NATIVE-MEMORY CAP (iOS 52 MB jetsam fix). Without an explicit QUIC
            // receive window, Network.framework advertises a large default, so on a download burst the
            // server floods each stream and the inbound data buffers in NATIVE memory faster than the
            // proxy drains it — phys_footprint spiked +16 MB in ~1 s at only ~25 streams and jetsam-killed
            // the extension. These windows are QUIC flow control: the server may not send more than the
            // window until we consume and send window updates, bounding native receive buffering.
            //   - bidi-local: per-stream window for streams WE open (the proxy's download streams).
            //   - InitialMaxData: connection-wide aggregate across all streams (the real ceiling).
            // 2026-07-02: tightened 64 KB/1 MB → 32 KB/256 KB. Even with the 1 MB aggregate the
            // downstream native transients (parsed packets + NSData copies retained by NE/nw past our
            // dispose) spiked phys_footprint +6.6 MB in 250 ms at full rate and jetsam-killed the
            // extension (dev baseline crashes too — pre-existing ceiling, exposed by restored
            // throughput). 256 KB caps tunnel-wide in-flight download per RTT (~20 Mbps @100 ms,
            // ~70 Mbps @30 ms): deliberately SLOWER but with a hard memory bound, per product call —
            // stability over top speed on iOS.
            quic.InitialMaxStreamDataBidirectionalLocal = 32 * 1024;
            quic.InitialMaxStreamDataBidirectionalRemote = 32 * 1024;
            quic.InitialMaxData = 256 * 1024;

            IosQuicTls.Configure(quic.SecProtocolOptions, options.TargetHost,
                options.CertificateValidationCallback, queue);
        });

        var connectionGroup = new NWConnectionGroup(multiplexGroup, parameters);

        // Queue for peer-initiated (inbound) streams. The handler must be wired before the group starts
        // so no incoming stream is missed; IosQuicConnection.AcceptInboundStreamAsync drains it.
        var inboundStreams = Channel.CreateUnbounded<NWConnection>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });

        try {
            connectionGroup.SetNewConnectionHandler(stream => {
                if (inboundStreams.Writer.TryWrite(stream))
                    return;

                VhUtils.TryInvoke(() => stream.Cancel());
                stream.Dispose();
            });

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var reg = cancellationToken.Register(() => tcs.TrySetCanceled());

            connectionGroup.SetStateChangedHandler((state, error) => {
                switch (state) {
                    case NWConnectionGroupState.Ready:
                        tcs.TrySetResult();
                        break;
                    case NWConnectionGroupState.Failed:
                    case NWConnectionGroupState.Cancelled:
                        tcs.TrySetException(new IOException($"QUIC tunnel failed to connect: {error}"));
                        break;
                }
            });

            connectionGroup.SetQueue(queue);
            connectionGroup.Start();

            await tcs.Task.Vhc();
            connectionGroup.SetStateChangedHandler(null!);
            return new IosQuicConnection(connectionGroup, multiplexGroup, endpoint, queue,
                options.RemoteEndPoint, inboundStreams);
        }
        catch {
            inboundStreams.Writer.TryComplete();
            VhUtils.TryInvoke(() => connectionGroup.SetStateChangedHandler(null!));
            VhUtils.TryInvoke(() => connectionGroup.SetNewConnectionHandler(null!));
            VhUtils.TryInvoke(() => connectionGroup.Cancel());
            connectionGroup.Dispose();
            multiplexGroup.Dispose();
            endpoint.Dispose();
            VhUtils.TryInvoke(queue.Dispose);
            throw;
        }
    }
}
