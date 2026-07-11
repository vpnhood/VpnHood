using System.Net;

namespace VpnHood.Core.Quic.Abstractions;

/// <summary>
/// An established QUIC connection over which lightweight bidirectional streams are opened (client)
/// or accepted (server). VpnHood uses QUIC purely as a transport, so streams are surfaced as plain
/// <see cref="Stream"/> instances.
/// </summary>
public interface IQuicConnection : IAsyncDisposable
{
    IPEndPoint LocalEndPoint { get; }
    IPEndPoint RemoteEndPoint { get; }

    /// <summary>Opens a new outbound bidirectional stream (client side).</summary>
    ValueTask<Stream> OpenOutboundStreamAsync(CancellationToken cancellationToken);

    /// <summary>Accepts the next inbound bidirectional stream (server side).</summary>
    ValueTask<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken);
}
