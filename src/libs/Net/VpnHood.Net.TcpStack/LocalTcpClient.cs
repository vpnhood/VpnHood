using System.Net;
using VpnHood.Net.TcpStack.Abstractions;

namespace VpnHood.Net.TcpStack;

/// <summary>
/// Represents a connection a <see cref="LocalTcpListener"/> accepted: its endpoints and the stream it owns.
/// </summary>
public sealed class LocalTcpClient : ITcpClient
{
    private readonly LocalTcpStream _stream;

    internal LocalTcpClient(LocalTcpStream stream, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint)
    {
        _stream = stream;
        LocalEndPoint = localEndPoint;
        RemoteEndPoint = remoteEndPoint;
    }

    /// <inheritdoc/>
    public IPEndPoint LocalEndPoint { get; }

    /// <inheritdoc/>
    public IPEndPoint RemoteEndPoint { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// A read returns what is buffered, which may be less than asked. After the peer's FIN, reads return the rest and
    /// then 0, and the stream can still write: a half-close. After a reset or an abort, reads also return what is
    /// buffered, then 0.
    /// </para>
    /// <para>
    /// A write completes once all its data has gone through the send path: each segment is buffered and handed to
    /// <see cref="LocalTcpStack.OnPacketSend"/>, waiting for room in the peer's window and in the buffer, which
    /// acknowledgments free. Its completion does not mean the peer acknowledged the data. It throws
    /// <see cref="IOException"/> once the connection is closed for writing, and <see cref="ObjectDisposedException"/>
    /// after disposal; a write that is canceled or fails may have sent part of the data.
    /// <see cref="Stream.FlushAsync(CancellationToken)"/> does nothing: data goes out as the window allows.
    /// <see cref="Stream.CanWrite"/> turns <see langword="false"/> once the connection is closed for writing.
    /// </para>
    /// <para>One read and one write may run at the same time.</para>
    /// </remarks>
    public Stream Stream => _stream;

    /// <summary>
    /// Disposes the stream and starts closing the connection: the peer gets a FIN, whose exchange the stack completes
    /// after this method returns. If received data is still unread, the peer gets a reset instead: at once when it has
    /// acknowledged everything sent, or else after the FIN, once it acknowledges the rest.
    /// </summary>
    public void Dispose() => _stream.Dispose();

    /// <summary>
    /// Disposes the stream and starts closing the connection, as <see cref="Dispose"/> does; it does not wait for the
    /// close to complete.
    /// </summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    public ValueTask DisposeAsync() => _stream.DisposeAsync();
}
