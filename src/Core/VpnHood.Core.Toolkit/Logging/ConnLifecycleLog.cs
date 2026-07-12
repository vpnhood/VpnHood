using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Memory;

namespace VpnHood.Core.Toolkit.Logging;

/// <summary>
/// Shared formatter for per-connection lifecycle diagnostic lines, so every subsystem (TcpStack
/// connections, iOS QUIC streams, ...) logs the SAME shape and the columns stay aligned when the lines
/// interleave in a live device console:
/// <c>[Tag] +CONN live=NNN mem=MM.MMB detail</c> / <c>[Tag] -CONN live=NNN mem=MM.MMB detail (reason)</c>.
/// Lines are Debug-level and low-frequency (one per connection/stream, never per-packet).
/// </summary>
public static class ConnLifecycleLog
{
    /// <summary>Logs a connection/stream open: fixed-width live/mem columns first, detail trails.</summary>
    public static void Opened(EventId eventId, string tag, int live, object detail)
    {
        VhLogger.Instance.LogDebug(eventId,
            "[{Tag}] +CONN live={Live} mem={Memory}MB {Detail}",
            tag, live.ToString("D3"), FootprintFixed(), detail);
    }

    /// <summary>Logs a connection/stream close: same columns as open; the teardown reason (if any) trails.</summary>
    public static void Closed(EventId eventId, string tag, int live, object detail, string? reason = null)
    {
        if (reason == null)
            VhLogger.Instance.LogDebug(eventId,
                "[{Tag}] -CONN live={Live} mem={Memory}MB {Detail}",
                tag, live.ToString("D3"), FootprintFixed(), detail);
        else
            VhLogger.Instance.LogDebug(eventId,
                "[{Tag}] -CONN live={Live} mem={Memory}MB {Detail} ({Reason})",
                tag, live.ToString("D3"), FootprintFixed(), detail, reason);
    }

    // Fixed-width (5 chars, F1: "  8.3", " 43.2", "102.4") so the mem column stays aligned across
    // +CONN/-CONN lines when eyeballing the live device console. The footprint (the number the host memory
    // limit is enforced against — e.g. iOS jetsam phys_footprint) comes from the device-set
    // VhMemory.Instance; null on platforms that don't supply it → "  n/a" keeps the width.
    private static string FootprintFixed()
    {
        var mb = VhMemory.Instance.GetInfo().ProcessFootprintMb;
        return mb.HasValue ? $"{mb.Value,5:F1}" : "  n/a";
    }
}
