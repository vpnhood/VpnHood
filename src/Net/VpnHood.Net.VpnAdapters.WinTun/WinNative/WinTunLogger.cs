using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.VpnAdapters.WinTun.WinNative;

// WinTun says why it failed only here, such as the problem code of a device Windows did not start: the
// error code it returns is bare
internal static class WinTunLogger
{
    // the DLL holds the pointer for the life of the process, so the delegate is never collected
    private static readonly WintunLoggerCallback Callback = Log;

    public static void Register()
    {
        WinTunApi.WintunSetLogger(Callback);
    }

    private static void Log(WintunLoggerLevel level, ulong timestamp, string message)
    {
        var logLevel = level switch {
            WintunLoggerLevel.Error => LogLevel.Error,
            WintunLoggerLevel.Warn => LogLevel.Warning,
            _ => LogLevel.Information
        };
        VhLogger.Instance.Log(logLevel, "WinTun: {Message}", message);
    }
}