using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Toolkit.Sockets;

/// <summary>
/// Optional settings applied while creating a TCP client. Per-call values override defaults supplied
/// by socket-factory decorators and are applied before the caller starts the TCP handshake.
/// </summary>
public sealed class TcpClientOptions
{
    /// <summary>
    /// Kernel socket send/receive buffer sizes (SO_SNDBUF/SO_RCVBUF), set as a pair: null leaves both
    /// at the OS default, which on Darwin also keeps its buffer autotune enabled. Named for the kernel
    /// because the managed copy buffers elsewhere in the transport are sized separately and cost
    /// different memory — these are charged once per socket, not per flow.
    /// </summary>
    public TransferBufferSize? KernelBufferSize { get; init; }
}
