using Microsoft.Extensions.Logging;

namespace VpnHood.Core.TcpStack.Abstractions;

/// <summary>
/// EventIds for TcpStack verbose logging.
/// Used to filter hot-path trace logs via VhLogger.Logged event.
/// </summary>
public static class TcpStackEventIds
{
    /// <summary>
    /// EventId for verbose TCP connection diagnostics (ACK, window, retransmission, etc.)
    /// </summary>
    public static readonly EventId TcpStackDiag = new(10001, "TcpStackVerbose");
}
