using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.Toolkit.Sockets;

/// <summary>
/// Optional settings applied while creating a TCP client. Per-call values override defaults supplied
/// by socket-factory decorators and are applied before the caller starts the TCP handshake.
/// </summary>
public sealed class TcpClientOptions
{
    /// <summary>
    /// Gets the kernel socket send and receive buffer sizes (SO_SNDBUF and SO_RCVBUF), each set when above zero, or
    /// <see langword="null"/> to leave both at the system's default. Named for the kernel because the managed copy
    /// buffers elsewhere in the transport are sized separately.
    /// </summary>
    public TransferBufferSize? KernelBufferSize { get; init; }
}
