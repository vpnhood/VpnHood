using Microsoft.Extensions.Logging;
using VpnHood.Core.Quic.Ios;
using VpnHood.Core.VpnAdapters.IosTun;

namespace VpnHood.Core.Client.Devices.Ios;

/// <summary>
/// The single master switch for ALL iOS investigation diagnostics. Setting <see cref="Enabled"/> fans out to
/// every per-subsystem gate (<see cref="IosQuicDiagnostics"/>, <see cref="IosTunDiagnostics"/>,
/// <see cref="IosMemoryMonitor"/>) so nothing needs to be flipped individually. Off by default = production.
/// </summary>
/// <remarks>
/// There is no dedicated switch: diagnostics follow the effective log level. Turning on debug logging in the
/// app UI (the <c>/log:debug</c> or <c>/log:trace</c> DebugCommand, or a debug-mode build) flows via
/// <c>ClientOptions.LogServiceOptions</c> into the Extension's LogService, and
/// <c>IosVpnService.CreateAdapter</c> calls <see cref="ApplyLogLevel"/> on every (re)connect — diagnostics
/// are on whenever the level is below Information, off otherwise.
/// </remarks>
public static class IosDiagnostics
{
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

    /// <summary>Sets <see cref="Enabled"/> from the effective log level: on when below Information.</summary>
    public static void ApplyLogLevel(LogLevel minLogLevel)
    {
        Enabled = minLogLevel < LogLevel.Information;
    }
}
