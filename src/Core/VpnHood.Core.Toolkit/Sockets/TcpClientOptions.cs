using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Toolkit.Sockets;

/// <summary>
/// Optional settings applied while creating a TCP client. Per-call values override defaults supplied
/// by socket-factory decorators and are applied before the caller starts the TCP handshake.
/// </summary>
public sealed class TcpClientOptions
{
    /// <summary>
    /// Kernel send/receive buffer sizes, set as a pair: null leaves both at the OS default.
    /// </summary>
    public TransferBufferSize? BufferSize { get; init; }
}
