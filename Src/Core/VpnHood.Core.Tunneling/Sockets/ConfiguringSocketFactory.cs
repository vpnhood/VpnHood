using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Common.Messaging;
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

    public TcpClient CreateTcpClient(IPEndPoint ipEndPoint)
    {
        return CreateTcpClient(ipEndPoint, tcpKernelBufferSize: null);
    }

    // Per-call buffer size wins; when null the class-level TcpKernelBufferSize is used.
    public TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TransferBufferSize? tcpKernelBufferSize)
    {
        var tcpClient = inner.CreateTcpClient(ipEndPoint);

        // Apply keep-alive / no-delay and the configured kernel socket buffer sizes. Null buffer leaves
        // the OS defaults/auto-tuning untouched; memory-constrained hosts can set small values to bound
        // the per-connection kernel memory charged to the process.
        var bufferSize = tcpKernelBufferSize ?? TcpKernelBufferSize;
        VhUtils.ConfigTcpClient(tcpClient,
            sendBufferSize: bufferSize?.Send,
            receiveBufferSize: bufferSize?.Receive,
            keepAlive: KeepAlive ? true : null,
            noDelay: NoDelay);

        return tcpClient;
    }

    public UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        return inner.CreateUdpClient(addressFamily);
    }
}
