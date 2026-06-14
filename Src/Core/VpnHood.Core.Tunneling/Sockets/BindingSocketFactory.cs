using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Sockets;

namespace VpnHood.Core.Tunneling.Sockets;

public class BindingSocketFactory(ISocketFactory socketFactory) : ISocketFactory
{
    public TcpClient CreateTcpClient(IPEndPoint ipEndPoint)
    {
        var client = socketFactory.CreateTcpClient(ipEndPoint);
        if (client.Client is null)
            throw new Exception("TcpClient socket is null.");

        // If the inner factory already bound the socket,
        if (client.Client.IsBound)
            return client;

        // If the inner factory already bound the socket,
        // leave it as is. Otherwise, bind to an ephemeral port on all interfaces
        var localEndPoint = new IPEndPoint(ipEndPoint.IsV4() ? IPAddress.Any : IPAddress.IPv6Any, 0);
        client.Client.Bind(localEndPoint);
        return client;
    }

    public UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var client = socketFactory.CreateUdpClient(addressFamily);
        if (client.Client is null)
            throw new Exception("UdpClient socket is null.");

        if (client.Client.IsBound)
            return client;

        // If the inner factory already bound the socket,
        // leave it as is. Otherwise, bind to an ephemeral port on all interfaces
        var localEndPoint = new IPEndPoint(addressFamily.IsV4() ? IPAddress.Any : IPAddress.IPv6Any, 0);
        client.Client.Bind(localEndPoint);
        return client;
    }
}