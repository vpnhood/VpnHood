using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Core.Tunneling.Sockets;

/// <summary>
/// Provides the sockets of another factory, each bound to an ephemeral port on all interfaces where that factory left
/// it unbound.
/// </summary>
/// <param name="socketFactory">The factory that makes the sockets.</param>
public class BindingSocketFactory(ISocketFactory socketFactory) : ISocketFactory
{
    /// <inheritdoc/>
    /// <remarks>It binds the client, where it is unbound, on the endpoint's IP version.</remarks>
    /// <exception cref="Exception">The factory gave a client without a socket.</exception>
    public TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
    {
        var client = socketFactory.CreateTcpClient(ipEndPoint, options);
        if (client.Client is null)
            throw new Exception("TcpClient socket is null.");

        if (client.Client.IsBound)
            return client;

        var localEndPoint = new IPEndPoint(ipEndPoint.IsV4 ? IPAddress.Any : IPAddress.IPv6Any, 0);
        client.Client.Bind(localEndPoint);
        return client;
    }

    /// <inheritdoc/>
    /// <remarks>It binds the client, where it is unbound.</remarks>
    /// <exception cref="Exception">The factory gave a client without a socket.</exception>
    public UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var client = socketFactory.CreateUdpClient(addressFamily);
        if (client.Client is null)
            throw new Exception("UdpClient socket is null.");

        if (client.Client.IsBound)
            return client;

        var localEndPoint = new IPEndPoint(addressFamily.IsV4 ? IPAddress.Any : IPAddress.IPv6Any, 0);
        client.Client.Bind(localEndPoint);
        return client;
    }

    /// <inheritdoc/>
    /// <remarks>It binds the socket, where it is unbound.</remarks>
    public Socket CreateUdpSocket(AddressFamily addressFamily)
    {
        var socket = socketFactory.CreateUdpSocket(addressFamily);
        if (socket.IsBound)
            return socket;

        var localEndPoint = new IPEndPoint(addressFamily.IsV4 ? IPAddress.Any : IPAddress.IPv6Any, 0);
        socket.Bind(localEndPoint);
        return socket;
    }
}
