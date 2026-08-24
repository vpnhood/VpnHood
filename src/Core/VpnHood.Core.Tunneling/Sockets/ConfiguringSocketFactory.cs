using VpnHood.Core.Toolkit.Net;
using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling.Sockets;

// Decorator that delegates creation to an inner factory and then applies the common post-creation
// config (keep-alive, no-delay and the kernel send/receive buffer sizes) to the returned TcpClient.
// Centralizes the config so inner factories only have to create/protect sockets.
public class ConfiguringSocketFactory(ISocketFactory inner) : ISocketFactory
{
    public bool KeepAlive { get; set; }
    public bool NoDelay { get; set; }

    // Class-level default applied when CreateTcpClient is not given a per-call size.
    // Settable so the size can be adjusted at runtime (e.g. server reconfiguration).
    public TransferBufferSize? TcpKernelBufferSize { get; set; }

    public TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
    {
        // A per-call buffer size wins as a whole pair; otherwise the configured factory default applies.
        var effectiveOptions = new TcpClientOptions {
            BufferSize = options?.BufferSize ?? TcpKernelBufferSize
        };
        var tcpClient = inner.CreateTcpClient(ipEndPoint, effectiveOptions);

        // Buffer settings were applied by the inner factory before returning the socket. Apply the
        // remaining common behavior here without overwriting those effective values.
        VhUtils.ConfigTcpClient(tcpClient,
            keepAlive: KeepAlive ? true : null,
            noDelay: NoDelay);

        return tcpClient;
    }

    public UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        return inner.CreateUdpClient(addressFamily);
    }

    public Socket CreateUdpSocket(AddressFamily addressFamily)
    {
        return inner.CreateUdpSocket(addressFamily);
    }

    public bool IsQuicSupported => inner.IsQuicSupported;
    public IQuicClient CreateQuicClient() => inner.CreateQuicClient();
}
