using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Toolkit.Sockets;

namespace VpnHood.Core.Tunneling.Sockets;

public class SystemSocketFactory : ISocketFactory
{
    public TcpClient CreateTcpClient(IPEndPoint ipEndPoint)
    {
        var tcpClient = new TcpClient(ipEndPoint.AddressFamily);
        return tcpClient;
    }

    public UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var udpClient = new UdpClient(addressFamily);
        return udpClient;
    }

    public bool IsQuicSupported => false;

    public IQuicClient CreateQuicClient() =>
        throw new NotSupportedException("QUIC is not supported by this socket factory.");
}