using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Quic.Abstractions;

namespace VpnHood.Core.Toolkit.Sockets;

public interface ISocketFactory
{
    public TcpClient CreateTcpClient(IPEndPoint ipEndPoint);
    public UdpClient CreateUdpClient(AddressFamily addressFamily);

    // Raw UDP socket for workers that manage their own receive buffer: UdpClient pins a fixed 64 KB
    // internal buffer per instance, too costly for small-buffer workers (e.g. DNS). The default routes
    // through CreateUdpClient so decorator behavior (protect/bind/configure) is preserved for factories
    // that don't override it; the discarded UdpClient wrapper has no finalizer, so the socket is safe.
    public Socket CreateUdpSocket(AddressFamily addressFamily) => CreateUdpClient(addressFamily).Client;

    // QUIC capability. A factory that cannot create QUIC clients returns false from IsQuicSupported
    // and throws NotSupportedException from CreateQuicClient. Decorators forward both to their inner factory.
    public bool IsQuicSupported { get; }
    public IQuicClient CreateQuicClient();
}
