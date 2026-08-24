using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Quic.Abstractions;

namespace VpnHood.Core.Toolkit.Sockets;

public class SystemSocketFactory : ISocketFactory
{
    public virtual TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
    {
        var tcpClient = new TcpClient(ipEndPoint.AddressFamily);
        var sendBufferSize = options?.SendBufferSize;
        var receiveBufferSize = options?.ReceiveBufferSize;
        if (sendBufferSize is > 0)
            tcpClient.SendBufferSize = sendBufferSize.Value;
        if (receiveBufferSize is > 0)
            tcpClient.ReceiveBufferSize = receiveBufferSize.Value;
        return tcpClient;
    }

    public virtual UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var udpClient = new UdpClient(addressFamily);
        return udpClient;
    }

    public virtual Socket CreateUdpSocket(AddressFamily addressFamily)
    {
        return new Socket(addressFamily, SocketType.Dgram, ProtocolType.Udp);
    }

    public virtual bool IsQuicSupported => false;
     
    public virtual IQuicClient CreateQuicClient() =>
        throw new NotSupportedException("QUIC is not supported by this socket factory.");
}
