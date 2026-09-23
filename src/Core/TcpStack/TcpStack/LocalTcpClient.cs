using System.Net;
using VpnHood.Core.TcpStack.Abstractions;

namespace VpnHood.Core.TcpStack;

/// <summary>
/// Represents an accepted TCP connection from the local TCP stack.
/// Similar to <see cref="System.Net.Sockets.TcpClient"/>, it holds endpoints and owns the stream.
/// </summary>
public sealed class LocalTcpClient(LocalTcpStream stream, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint)
    : ITcpClient
{
    public IPEndPoint LocalEndPoint { get; } = localEndPoint;
    public IPEndPoint RemoteEndPoint { get; } = remoteEndPoint;
    public Stream Stream => stream;

    public void Dispose() => stream.Dispose();
    public ValueTask DisposeAsync() => stream.DisposeAsync();
}
