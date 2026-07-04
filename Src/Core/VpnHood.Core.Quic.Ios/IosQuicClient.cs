using System.Threading.Channels;
using CoreFoundation;
using Network;
using ObjCRuntime;
using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Quic.Ios;

/// <summary>
/// Client-side QUIC connector for iOS, backed by Apple's Network.framework (iOS 15+). Establishes a
/// multiplexed QUIC tunnel and exposes lightweight streams over it, matching the desktop MsQuic client.
/// </summary>
/// <param name="initialMaxStreamDataBidirectionalLocal">Per-stream QUIC receive window (bytes) for streams
/// this client opens (the proxy's download streams). <c>null</c> keeps Network.framework's OS default.</param>
/// <param name="initialMaxStreamDataBidirectionalRemote">Per-stream QUIC receive window (bytes) for
/// peer-opened streams. <c>null</c> keeps the OS default.</param>
/// <param name="initialMaxData">Connection-wide aggregate QUIC receive window (bytes) across all streams —
/// the real ceiling. <c>null</c> keeps the OS default. On iOS the factory sets these as the ~52 MB jetsam
/// fix; see <see cref="IosSocketFactory"/>.</param>
public sealed class IosQuicClient(
    ulong? initialMaxStreamDataBidirectionalLocal = null,
    ulong? initialMaxStreamDataBidirectionalRemote = null,
    ulong? initialMaxData = null)
    : IQuicClient
{
    public static bool IsSupported => OperatingSystem.IsIOSVersionAtLeast(15);

    // DIAGNOSTIC counters (live QUIC stream count, in-flight send bytes, teardown timing, [VHQUIC] logs)
    // live in IosQuicDiagnostics — maintained only when that switch is on.
    //
    // JETSAM GUARD input: the extension's live phys_footprint in MB, read on demand via
    // VhMemory.Instance (IosMemory, registered by the device/service). At full download rate
    // (~130 Mbps) the per-packet native transients (NSData copies retained briefly by NE/nw) float several MB
    // and their peaks ratchet over the 52 MB limit with no leak, freeze, or backlog anywhere we can bound
    // directly (2026-07-01 third crash flavor). IosQuicStream.ReadAsync brakes download intake while the
    // footprint is within a few MB of the limit, letting the transients drain. No provider set -> guard inactive.

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
            // NWProtocolOptions) — which, thrown on this native trampoline thread, aborts the
            // process with SIGABRT.
            // Re-wrap the SAME native handle as NWProtocolQuicOptions (owns:false — the parameters own
            // the native object); setters then mutate the real options block used by the connection.
            var quic = Runtime.GetINativeObject<NWProtocolQuicOptions>(quicOptions.Handle, owns: false)
                ?? throw new IOException("Failed to access QUIC protocol options.");
            quic.AddTlsApplicationProtocol("h3");
            quic.InitialMaxStreamsBidirectional = (ulong)options.MaxInboundBidirectionalStreams;

            // QUIC receive-window flow control (backpressure). Each window is applied only when the caller
            // configured it; leaving one null keeps Network.framework's (large) OS default for that window.
            // On iOS these are set to tight caps by the factory as the ~52 MB jetsam fix — without them the
            // server floods each stream and native receive buffering blows the limit. See IosSocketFactory.
            if (initialMaxStreamDataBidirectionalLocal.HasValue)
                quic.InitialMaxStreamDataBidirectionalLocal = initialMaxStreamDataBidirectionalLocal.Value;
            if (initialMaxStreamDataBidirectionalRemote.HasValue)
                quic.InitialMaxStreamDataBidirectionalRemote = initialMaxStreamDataBidirectionalRemote.Value;
            if (initialMaxData.HasValue)
                quic.InitialMaxData = initialMaxData.Value;

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

                VhUtils.TryInvoke(stream.Cancel);
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
            VhUtils.TryInvoke(connectionGroup.Cancel);
            connectionGroup.Dispose();
            multiplexGroup.Dispose();
            endpoint.Dispose();
            VhUtils.TryInvoke(queue.Dispose);
            throw;
        }
    }
}
