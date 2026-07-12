using VpnHood.Core.Quic.Ios;
using VpnHood.Core.VpnAdapters.IosTun;

namespace VpnHood.Core.Client.Devices.Ios;

/// <summary>
/// The single master switch for ALL iOS investigation diagnostics. Setting <see cref="Enabled"/> fans out to
/// every per-subsystem gate (<see cref="IosQuicDiagnostics"/>, <see cref="IosTunDiagnostics"/>,
/// <see cref="IosMemoryMonitor"/>) so nothing needs to be flipped individually. Off by default = production.
/// </summary>
/// <remarks>
/// Enabled through the app UI: add <c>/mem-diagnostics</c> to Debug Data 1 (a standard DebugCommand). It
/// travels the existing debug-data path — <c>UserSettings.DebugData1</c> → <c>ClientOptions.DebugData1</c> →
/// <c>vpn.config</c> → <c>VpnServiceHost</c> → <c>IosVpnService.CreateAdapter</c>, which calls
/// <see cref="ApplyDebugData"/> in the Extension process. No environment variable is involved: iOS spawns
/// the Extension itself, so a devicectl launch env could never reach it anyway. The same command also drops
/// the log level to Debug (see <c>VpnHoodApp.GetLogOptions</c>) so the [VHQUIC]/+CONN traces surface.
/// </remarks>
public static class IosDiagnostics
{
    /// <summary>Must equal <c>DebugCommands.MemDiagnostics</c> (AppLib cannot be referenced from here).</summary>
    public const string DebugCommand = "/mem-diagnostics";

    private static bool _enabled;

    /// <summary>Master switch; assigning it fans out to all per-subsystem diagnostic gates.</summary>
    public static bool Enabled {
        get => _enabled;
        set {
            _enabled = value;
            IosQuicDiagnostics.Enabled = value;
            IosTunDiagnostics.Enabled = value;
            IosMemoryMonitor.Enabled = value;
        }
    }

    /// <summary>
    /// Sets <see cref="Enabled"/> from a ClientOptions debug-data string (space-separated commands, matched
    /// case-insensitively — the same semantics as <c>VpnHoodApp.HasDebugCommand</c>).
    /// </summary>
    public static void ApplyDebugData(string? debugData)
    {
        Enabled = debugData?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(DebugCommand, StringComparer.OrdinalIgnoreCase) == true;
    }
}
