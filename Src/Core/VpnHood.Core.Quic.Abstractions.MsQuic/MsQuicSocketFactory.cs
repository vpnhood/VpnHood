using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Toolkit.Sockets;

namespace VpnHood.Core.Quic.Abstractions.MsQuic;

/// <summary>
/// An <see cref="ISocketFactory"/> decorator that adds MsQuic-based QUIC client support on platforms
/// where MsQuic is available (Windows/Linux). TCP/UDP creation is delegated to the inner factory.
/// </summary>
public class MsQuicSocketFactory(ISocketFactory inner) : ISocketFactory
{
    public TcpClient CreateTcpClient(IPEndPoint ipEndPoint) => inner.CreateTcpClient(ipEndPoint);
    public UdpClient CreateUdpClient(AddressFamily addressFamily) => inner.CreateUdpClient(addressFamily);
    public bool IsQuicSupported => MsQuicClient.IsSupported;

    public IQuicClient CreateQuicClient() => MsQuicClient.IsSupported
        ? new MsQuicClient()
        : throw new NotSupportedException("QUIC is not supported on this platform.");
}
