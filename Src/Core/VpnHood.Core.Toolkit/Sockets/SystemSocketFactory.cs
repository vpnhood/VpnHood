using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Quic.Abstractions;

namespace VpnHood.Core.Toolkit.Sockets;

public class SystemSocketFactory : ISocketFactory
{
    public virtual TcpClient CreateTcpClient(IPEndPoint ipEndPoint)
    {
        var tcpClient = new TcpClient(ipEndPoint.AddressFamily);
        return tcpClient;
    }

    public virtual UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var udpClient = new UdpClient(addressFamily);
        return udpClient;
    }

    public virtual bool IsQuicSupported => false;
     
    public virtual IQuicClient CreateQuicClient() =>
        throw new NotSupportedException("QUIC is not supported by this socket factory.");
}