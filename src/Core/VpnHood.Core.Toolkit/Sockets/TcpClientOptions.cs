namespace VpnHood.Core.Toolkit.Sockets;

/// <summary>
/// Optional settings applied while creating a TCP client. Per-call values override defaults supplied
/// by socket-factory decorators and are applied before the caller starts the TCP handshake.
/// </summary>
public sealed class TcpClientOptions
{
    public int? SendBufferSize { get; init; }
    public int? ReceiveBufferSize { get; init; }
}
