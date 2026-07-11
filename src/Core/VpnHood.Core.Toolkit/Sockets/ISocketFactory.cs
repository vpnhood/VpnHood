using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Quic.Abstractions;

namespace VpnHood.Core.Toolkit.Sockets;

public interface ISocketFactory
{
    public TcpClient CreateTcpClient(IPEndPoint ipEndPoint);
    public UdpClient CreateUdpClient(AddressFamily addressFamily);

    // QUIC capability. A factory that cannot create QUIC clients returns false from IsQuicSupported
    // and throws NotSupportedException from CreateQuicClient. Decorators forward both to their inner factory.
    public bool IsQuicSupported { get; }
    public IQuicClient CreateQuicClient();
}
