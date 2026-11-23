using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>
/// Represents a connection made through a proxy: the socket to the proxy, and the stream that carries the destination's
/// bytes over it.
/// </summary>
/// <param name="tcpClient">The socket to the proxy.</param>
/// <param name="stream">
/// The stream to read and write the destination's bytes through: the socket's own, or, for an HTTPS proxy, the TLS
/// session with the proxy over it, which the socket's own stream would pass by.
/// </param>
public sealed class ProxyConnection(TcpClient tcpClient, Stream stream) : IDisposable
{
    /// <summary>Gets the socket to the proxy, which also tells the connection apart for its proxy connector.</summary>
    public TcpClient TcpClient { get; } = tcpClient;

    /// <summary>Gets the stream to read and write the destination's bytes through.</summary>
    public Stream Stream { get; } = stream;

    /// <summary>Closes the stream and the socket; a failed close of the stream is logged, and the socket closed.</summary>
    public void Dispose()
    {
        Stream.TryDispose(LogLevel.Warning);
        TcpClient.Dispose();
    }
}
