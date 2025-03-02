using System.Runtime.InteropServices;

namespace VpnHood.Core.VpnAdapters.WinTun.WinNative;

internal class Kernel32
{
    [DllImport("kernel32", SetLastError = true, ExactSpelling = true)]
    public static extern uint WaitForSingleObject(IntPtr handle, int milliseconds);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    public static extern bool CloseHandle(IntPtr hObject);
}
