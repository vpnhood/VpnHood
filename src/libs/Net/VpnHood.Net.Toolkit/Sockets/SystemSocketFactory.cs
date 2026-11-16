using System.Net;
using System.Net.Sockets;

namespace VpnHood.Net.Toolkit.Sockets;

/// <summary>
/// Provides the system's plain sockets: the base of the platform factories.
/// </summary>
public class SystemSocketFactory : ISocketFactory
{
    /// <inheritdoc/>
    /// <remarks>It sets each kernel buffer size the options give that is above zero.</remarks>
    public virtual TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
    {
        var tcpClient = new TcpClient(ipEndPoint.AddressFamily);
        if (options?.KernelBufferSize is { } bufferSize) {
            if (bufferSize.Send > 0)
                tcpClient.SendBufferSize = bufferSize.Send;
            if (bufferSize.Receive > 0)
                tcpClient.ReceiveBufferSize = bufferSize.Receive;
        }
        return tcpClient;
    }

    /// <inheritdoc/>
    public virtual UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var udpClient = new UdpClient(addressFamily);
        return udpClient;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It makes the socket itself, not through <see cref="CreateUdpClient"/>, so a subclass that overrides only that
    /// method does not change it.
    /// </remarks>
    public virtual Socket CreateUdpSocket(AddressFamily addressFamily)
    {
        return new Socket(addressFamily, SocketType.Dgram, ProtocolType.Udp);
    }
}
