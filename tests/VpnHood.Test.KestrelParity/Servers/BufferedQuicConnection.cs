using System.Net;
using VpnHood.Net.Quic.Abstractions;

namespace VpnHood.Test.KestrelParity.Servers;

internal sealed class BufferedQuicConnection(IQuicServerConnection connection, BufferedQuicServer server)
    : IQuicServerConnection
{
    public IPEndPoint LocalEndPoint => connection.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => connection.RemoteEndPoint;

    public ValueTask<Stream> OpenOutboundStreamAsync(CancellationToken cancellationToken) =>
        connection.OpenOutboundStreamAsync(cancellationToken);

    public ValueTask<Stream> OpenOutboundUnidirectionalStreamAsync(CancellationToken cancellationToken) =>
        connection.OpenOutboundUnidirectionalStreamAsync(cancellationToken);

    public async ValueTask<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken)
    {
        var stream = await connection.AcceptInboundStreamAsync(cancellationToken);
        try {
            if (stream is not IQuicStream quic)
                throw new InvalidOperationException("The test listener requires QUIC stream controls.");
            if (!quic.CanRead || !quic.CanWrite || !server.TryClaimRequest())
                return stream;
            // Preserve the request bytes while making parser progress independent of native receive timing.
            var prefix = new byte[server.ByteCount];
            await stream.ReadExactlyAsync(prefix, cancellationToken);
            if (server.Finish) {
                var end = new byte[1];
                if (await stream.ReadAsync(end, cancellationToken) != 0)
                    throw new InvalidOperationException("The buffered request contains data beyond its expected end.");
            }
            return new BufferedQuicStream(stream, quic, prefix, server.HeaderByteCount, server.Gate, server.Finish);
        }
        catch {
            await stream.DisposeAsync();
            throw;
        }
    }

    public ValueTask CloseAsync(long errorCode, CancellationToken cancellationToken) =>
        connection.CloseAsync(errorCode, cancellationToken);

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}
