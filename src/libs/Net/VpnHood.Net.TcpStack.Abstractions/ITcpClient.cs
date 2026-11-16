using System.Net;

namespace VpnHood.Net.TcpStack.Abstractions;

/// <summary>
/// Represents a connection an <see cref="ITcpListener"/> accepted, the way <see cref="System.Net.Sockets.TcpClient"/>
/// does for a socket. Disposing it closes the connection.
/// </summary>
public interface ITcpClient : IAsyncDisposable, IDisposable
{
    /// <summary>Gets the connection's local endpoint: the address and port the peer connected to.</summary>
    IPEndPoint LocalEndPoint { get; }

    /// <summary>Gets the connection's remote endpoint: the peer's address and port.</summary>
    IPEndPoint RemoteEndPoint { get; }

    /// <summary>Gets the stream that reads and writes the connection's data.</summary>
    Stream Stream { get; }
}
