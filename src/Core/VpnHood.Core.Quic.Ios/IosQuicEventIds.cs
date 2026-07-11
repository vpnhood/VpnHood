using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Quic.Ios;

/// <summary>
/// EventId for iOS QUIC diagnostics (mirrors <c>TcpStackEventIds</c>).
/// Lets a logger distinguish the <c>[VHQUIC]</c> logs from other subsystems (e.g. TcpStack) by EventId.
/// </summary>
public static class IosQuicEventIds
{
    /// <summary>EventId for all iOS QUIC diagnostics (stream +open/-close lifecycle + the jetsam-brake summary).</summary>
    public static readonly EventId Quic = new(10002, "Quic");
}
