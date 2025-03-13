namespace VpnHood.Core.VpnAdapters.WinTun.WinNative;

internal static class WinTunLogger
{
    public static void SetLogger(WintunLoggerCallback logger)
    {
        WinTunApi.WintunSetLogger(logger);
    }

    public static void ResetLogger()
    {
        WinTunApi.WintunSetLogger(IntPtr.Zero);
    }
}