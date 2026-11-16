using System.Runtime.InteropServices;

namespace VpnHood.Net.VpnAdapters.WinTun.WinNative;

internal delegate void WintunLoggerCallback(
    WintunLoggerLevel level, ulong timestamp, [MarshalAs(UnmanagedType.LPWStr)] string message);