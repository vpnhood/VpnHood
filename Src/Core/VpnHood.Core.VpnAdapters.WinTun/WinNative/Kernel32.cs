using System.Runtime.InteropServices;

namespace VpnHood.Core.VpnAdapters.WinTun.WinNative;

internal class Kernel32
{
    public const uint WaitObject0 = 0;
    public const uint Infinite = 0xFFFFFFFF;
    public const uint WaitFailed = 0xFFFFFFFF;

    [DllImport("kernel32", SetLastError = true, ExactSpelling = true)]
    public static extern uint WaitForSingleObject(IntPtr handle, int milliseconds);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
}
