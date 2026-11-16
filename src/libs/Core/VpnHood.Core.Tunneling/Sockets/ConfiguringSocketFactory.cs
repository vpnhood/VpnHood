using VpnHood.Net.Toolkit.Net;
using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Tunneling.Sockets;

/// <summary>
/// Provides the sockets of another factory, with the common settings on its TCP clients: keep-alive, no delay, and the
/// kernel's send and receive buffer sizes.
/// </summary>
/// <param name="inner">The factory that makes the sockets.</param>
/// <remarks>It keeps those settings in one place, so the inner factories only make and protect sockets.</remarks>
public class ConfiguringSocketFactory(ISocketFactory inner) : ISocketFactory
{
    /// <summary>
    /// Gets or sets a value indicating whether the TCP clients send keep-alives; where <see langword="false"/>, a
    /// client keeps its own setting.
    /// </summary>
    public bool KeepAlive { get; set; }

    /// <summary>Gets or sets a value indicating whether the TCP clients send without Nagle's delay.</summary>
    public bool NoDelay { get; set; }

    /// <summary>
    /// Gets or sets the kernel's send and receive buffer sizes for a TCP client asked for without options of its own,
    /// or <see langword="null"/> for the system's. It may change at any time, as a server's reconfiguration does.
    /// </summary>
    public TransferBufferSize? TcpKernelBufferSize { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// Options given win as a whole: a <see langword="null"/> kernel buffer size in them means the system's, not this
    /// factory's.
    /// </remarks>
    public TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
    {
        // Per-call options win as a whole: a caller that passes them has decided this socket's sizing,
        // including a null KernelBufferSize meaning "leave it at the OS default". Falling back per-property
        // would make that decision unexpressable — the caller could ask for any size except none, and
        // would silently inherit a factory default chosen for entirely different sockets.
        var effectiveOptions = options ?? new TcpClientOptions { KernelBufferSize = TcpKernelBufferSize };
        var tcpClient = inner.CreateTcpClient(ipEndPoint, effectiveOptions);

        // Buffer settings were applied by the inner factory before returning the socket. Apply the
        // remaining common behavior here without overwriting those effective values.
        VhUtils.ConfigTcpClient(tcpClient,
            keepAlive: KeepAlive ? true : null,
            noDelay: NoDelay);

        return tcpClient;
    }

    /// <inheritdoc/>
    /// <remarks>It is the inner factory's, as it is.</remarks>
    public UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        return inner.CreateUdpClient(addressFamily);
    }

    /// <inheritdoc/>
    /// <remarks>It is the inner factory's, as it is.</remarks>
    public Socket CreateUdpSocket(AddressFamily addressFamily)
    {
        return inner.CreateUdpSocket(addressFamily);
    }
}
