using System.Net;
using System.Threading.Channels;
using CoreFoundation;
using Network;
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
        // Open a new bidirectional outgoing stream over the existing QUIC tunnel.
        // NOTE (verify on-device): for a multiplex group, a null endpoint asks Network.framework to
        // create a brand-new stream over the group's tunnel; we pass the tunnel endpoint + fresh quic
        // options. If extraction returns null, the tunnel is no longer able to open streams.
        var streamOptions = new NWProtocolQuicOptions { StreamIsUnidirectional = false };
        var stream = connectionGroup.ExtractConnection(endpoint, streamOptions)
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
                        tcs.TrySetException(new IOException($"QUIC stream failed to start: {error}"));
                        break;
                }
            });

            stream.SetQueue(queue);
            stream.Start();

            await tcs.Task.Vhc();
            return new IosQuicStream(stream);
        }
        catch {
            try { stream.Cancel(); } catch { /* ignore */ }
            stream.Dispose();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        // Stop accepting, then drain and discard any inbound streams that were never accepted.
        inboundStreams.Writer.TryComplete();
        while (inboundStreams.Reader.TryRead(out var pending)) {
            try { pending.Cancel(); } catch { /* ignore */ }
            pending.Dispose();
        }

        try { connectionGroup.Cancel(); } catch { /* ignore */ }
        connectionGroup.Dispose();
        multiplexGroup.Dispose();
        endpoint.Dispose();
        return ValueTask.CompletedTask;
    }
}
