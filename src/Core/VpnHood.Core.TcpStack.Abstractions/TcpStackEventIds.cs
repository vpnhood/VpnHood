using Microsoft.Extensions.Logging;

namespace VpnHood.Core.TcpStack.Abstractions;

/// <summary>
/// EventIds for TcpStack verbose logging.
/// Used to filter hot-path trace logs via VhLogger.Logged event.
/// </summary>
public static class TcpStackEventIds
{
    /// <summary>
    /// EventId for all TcpStack diagnostics (connection lifecycle + verbose ACK/window/retransmission traces).
    /// Lets a logger distinguish TcpStack logs from other subsystems (e.g. QUIC) by EventId.
    /// </summary>
    public static readonly EventId TcpStack = new(10001, "TcpStack");
}
