using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Quic.Abstractions;

namespace VpnHood.Core.Toolkit.Sockets;

public class SystemSocketFactory : ISocketFactory
{
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
