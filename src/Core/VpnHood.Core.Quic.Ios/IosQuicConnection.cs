using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using CoreFoundation;
using Network;
using ObjCRuntime;
using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Quic.Ios;

/// <summary>
/// A QUIC connection backed by a Network.framework multiplexed tunnel
/// (<see cref="NWMultiplexGroup"/> + <see cref="NWConnectionGroup"/>). Outbound streams are extracted
/// from the group; inbound (peer-initiated) streams are delivered by the group's new-connection handler
/// and queued in <paramref name="inboundStreams"/>.
/// </summary>
internal sealed class IosQuicConnection(
    NWConnectionGroup connectionGroup,
    NWMultiplexGroup multiplexGroup,
    NWEndpoint endpoint,
    DispatchQueue queue,
    IPEndPoint remoteEndPoint,
    Channel<NWConnection> inboundStreams) : IQuicConnection
{
    public IPEndPoint RemoteEndPoint => remoteEndPoint;

    // Network.framework does not surface the local UDP endpoint of a QUIC tunnel; VpnHood uses these
    // only for diagnostics, so a family-appropriate placeholder is sufficient.
    public IPEndPoint LocalEndPoint { get; } =
        new(remoteEndPoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? IPAddress.IPv6Any
            : IPAddress.Any, 0);

    // Circuit breaker for outbound stream opens. nw_connection_group_extract_connection returns NULL when
    // the tunnel cannot create another stream — typically QUIC stream-credit starvation (the cumulative
    // MAX_STREAMS budget is spent and the server has not granted more). Every new proxied flow retries the
    // open immediately, so without a backoff a starved tunnel gets hammered with native extract calls and
    // connector churn that deepen the stall (observed on-device 2026-07-11: repeated "Failed to open a new
    // QUIC stream" bursts during a 37 s tunnel-wide stall). While in backoff, opens fail fast without
    // touching the native group; flows retry after the window with fresh credit odds.
    private const int OpenFailBackoffMs = 500;
    private readonly int _diagnosticId = IosQuicDiagnostics.OnConnectionOpened();
    private long _openFailBackoffUntilTick;
    private bool _disposed;

    public async ValueTask<Stream> OpenOutboundStreamAsync(CancellationToken cancellationToken)
    {
        if (Environment.TickCount64 < Volatile.Read(ref _openFailBackoffUntilTick))
            throw new IOException(
                "Failed to open a new QUIC stream over the tunnel (backing off after a failed open).");

        // Open a new bidirectional outgoing stream over the existing QUIC tunnel. For a multiplex group a
        // NULL endpoint tells Network.framework to create a brand-new stream over the group's existing
        // tunnel (this is what Swift's NWConnection(from: group) does). The managed
        // NWConnectionGroup.ExtractConnection NREs on a null endpoint and fails with a generic NWError on
        // Start() when given the tunnel endpoint, so call the native function directly with NULL.
        // Pass NULL endpoint AND NULL protocol options: this is exactly Swift's NWConnection(from: group),
        // which extracts a new bidirectional stream that inherits the group's tunnel + QUIC options. A
        // fresh NWProtocolQuicOptions instead makes the extracted connection try to stand up its own
        // transport and Start() fails with POSIX error 50 (network down).
        var streamHandle = nw_connection_group_extract_connection(
            connectionGroup.GetCheckedHandle(), IntPtr.Zero, IntPtr.Zero);
        var stream = Runtime.GetINativeObject<NWConnection>(streamHandle, owns: true);
        if (stream == null) {
            Volatile.Write(ref _openFailBackoffUntilTick, Environment.TickCount64 + OpenFailBackoffMs);
            throw new IOException("Failed to open a new QUIC stream over the tunnel.");
        }

        return await StartStreamAsync(stream, cancellationToken).Vhc();
    }

    public async ValueTask<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken)
    {
        // Streams the remote peer opens are delivered to the group's new-connection handler (wired in
        // IosQuicClient before the group started) and queued here; take the next one and bring it up.
        // Throws ChannelClosedException once the connection is disposed, ending any accept loop.
        var stream = await inboundStreams.Reader.ReadAsync(cancellationToken).Vhc();
        return await StartStreamAsync(stream, cancellationToken).Vhc();
    }

    // Brings a stream (outbound or inbound) up on the group's queue and waits until it is Ready.
    private async ValueTask<Stream> StartStreamAsync(NWConnection stream, CancellationToken cancellationToken)
    {
        try {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var reg = cancellationToken.Register(() => tcs.TrySetCanceled());

            stream.SetStateChangeHandler((state, error) => {
                try {
                    switch (state) {
                        case NWConnectionState.Ready:
                            tcs.TrySetResult();
                            break;
                        case NWConnectionState.Failed:
                        case NWConnectionState.Cancelled:
                            tcs.TrySetException(new IOException(
                                $"QUIC stream failed to start: state={state} domain={error?.ErrorDomain} code={error?.ErrorCode}"));
                            break;
                    }
                }
                finally {
                    error?.Dispose();
                }
            });

            stream.SetQueue(queue);
            stream.Start();

            await tcs.Task.Vhc();
            stream.SetStateChangeHandler(null!);
            var iosQuicStream = new IosQuicStream(stream);
            Toolkit.Memory.VhTypeTracker.Track(iosQuicStream);
            return iosQuicStream;
        }
        catch {
            stream.TrySetStateChangeHandler(null);
            stream.TryCancel();
            stream.Dispose();
            throw;
        }
    }

    // Native Network.framework entry point. The managed NWConnectionGroup.ExtractConnection binding
    // requires a non-null endpoint (it dereferences it), but opening a new QUIC stream over a multiplex
    // group requires a NULL endpoint. Returns a +1-retained nw_connection_t (owns:true on wrap).
    [DllImport("/System/Library/Frameworks/Network.framework/Network")]
    private static extern IntPtr nw_connection_group_extract_connection(
        IntPtr group, IntPtr endpoint, IntPtr protocolOptions);

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return ValueTask.CompletedTask;

        // Stop accepting, then drain and discard any inbound streams that were never accepted.
        inboundStreams.Writer.TryComplete();
        while (inboundStreams.Reader.TryRead(out var pending)) {
            pending.TryCancel();
            pending.Dispose();
        }

        connectionGroup.TrySetStateChangedHandler(null);
        connectionGroup.TrySetNewConnectionHandler(null);
        connectionGroup.TryCancel();
        connectionGroup.Dispose();
        multiplexGroup.Dispose();
        endpoint.Dispose();
        VhUtils.TryInvoke(queue.Dispose);
        IosQuicDiagnostics.OnConnectionClosed(_diagnosticId);
        return ValueTask.CompletedTask;
    }
}
