using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Toolkit.Sockets;

namespace VpnHood.Core.Quic.Ios;

/// <summary>
/// An <see cref="ISocketFactory"/> decorator that adds iOS (Network.framework) QUIC client support.
/// TCP/UDP creation is delegated to the inner factory.
/// </summary>
public class IosQuicSocketFactory(ISocketFactory inner) : ISocketFactory
{
    public TcpClient CreateTcpClient(IPEndPoint ipEndPoint) => inner.CreateTcpClient(ipEndPoint);
    public UdpClient CreateUdpClient(AddressFamily addressFamily) => inner.CreateUdpClient(addressFamily);
    public bool IsQuicSupported => IosQuicClient.IsSupported;

    public IQuicClient CreateQuicClient() => IosQuicClient.IsSupported
        ? new IosQuicClient()
        : throw new NotSupportedException("QUIC is not supported on this platform.");
}
