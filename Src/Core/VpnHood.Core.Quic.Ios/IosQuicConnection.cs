using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using CoreFoundation;
using Network;
using ObjCRuntime;
using VpnHood.Core.Quic.Abstractions;
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

    public async ValueTask<Stream> OpenOutboundStreamAsync(CancellationToken cancellationToken)
    {
        // Open a new bidirectional outgoing stream over the existing QUIC tunnel. For a multiplex group a
        // NULL endpoint tells Network.framework to create a brand-new stream over the group's existing
        // tunnel (this is what Swift's NWConnection(from: group) does). The managed
        // NWConnectionGroup.ExtractConnection NREs on a null endpoint and fails with a generic NWError on
        // Start() when given the tunnel endpoint, so call the native function directly with NULL.
        // Pass NULL endpoint AND NULL protocol options: this is exactly Swift's NWConnection(from: group),
        // which extracts a new bidirectional stream that inherits the group's tunnel + QUIC options. A
        // fresh NWProtocolQuicOptions instead makes the extracted connection try to stand up its own
        // transport and Start() fails with ENETDOWN (Posix 50).
        var streamHandle = nw_connection_group_extract_connection(
            (IntPtr)connectionGroup.GetCheckedHandle(), IntPtr.Zero, IntPtr.Zero);
        var stream = Runtime.GetINativeObject<NWConnection>(streamHandle, owns: true)
            ?? throw new IOException("Failed to open a new QUIC stream over the tunnel.");

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
            });

            stream.SetQueue(queue);
            stream.Start();

            await tcs.Task.Vhc();
            stream.SetStateChangeHandler(null!);
            return new IosQuicStream(stream);
        }
        catch {
            VhUtils.TryInvoke(() => stream.SetStateChangeHandler(null!));
            VhUtils.TryInvoke(() => stream.Cancel());
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
        // Stop accepting, then drain and discard any inbound streams that were never accepted.
        inboundStreams.Writer.TryComplete();
        while (inboundStreams.Reader.TryRead(out var pending)) {
            VhUtils.TryInvoke(() => pending.Cancel());
            pending.Dispose();
        }

        VhUtils.TryInvoke(() => connectionGroup.SetStateChangedHandler(null!));
        VhUtils.TryInvoke(() => connectionGroup.SetNewConnectionHandler(null!));
        VhUtils.TryInvoke(() => connectionGroup.Cancel());
        connectionGroup.Dispose();
        multiplexGroup.Dispose();
        endpoint.Dispose();
        VhUtils.TryInvoke(queue.Dispose);
        return ValueTask.CompletedTask;
    }
}
