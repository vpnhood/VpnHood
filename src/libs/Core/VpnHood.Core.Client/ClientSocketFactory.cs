using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Core.Client;

/// <summary>
/// The VPN client's factory made of a socket factory and the platform's QUIC.
/// </summary>
/// <param name="socketFactory">Creates the sockets.</param>
/// <param name="quicClientFactory">The platform's QUIC, or null where it has none.</param>
public class ClientSocketFactory(ISocketFactory socketFactory, IQuicClientFactory? quicClientFactory)
    : IClientSocketFactory
{
    /// <inheritdoc/>
    public IQuicClientFactory? QuicClientFactory => quicClientFactory;

    /// <inheritdoc/>
    /// <remarks>It is the socket factory's.</remarks>
    public TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null) =>
        socketFactory.CreateTcpClient(ipEndPoint, options);

    /// <inheritdoc/>
    /// <remarks>It is the socket factory's.</remarks>
    public UdpClient CreateUdpClient(AddressFamily addressFamily) =>
        socketFactory.CreateUdpClient(addressFamily);

    /// <inheritdoc/>
    /// <remarks>It is the socket factory's.</remarks>
    public Socket CreateUdpSocket(AddressFamily addressFamily) =>
        socketFactory.CreateUdpSocket(addressFamily);
}
