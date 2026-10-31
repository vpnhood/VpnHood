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
            if (!quic.CanRead || !quic.CanWrite)
                return stream;
            var request = server.TakeRequest();
            if (request == null)
                return stream;
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
                request.CancellationToken);
            // Present the complete probe to the production parser in one available prefix.
            var prefix = new byte[request.ByteCount];
            await stream.ReadExactlyAsync(prefix, lifetime.Token);
            if (request.Finish) {
                // The available-only parser must see FIN in the same buffered input as the DATA.
                var end = new byte[1];
                if (await stream.ReadAsync(end, lifetime.Token) != 0)
                    throw new InvalidOperationException("The buffered request contains data beyond its expected end.");
            }
            return new BufferedQuicStream(stream, quic, prefix, request.OnPrefixConsumed, request.Finish);
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
