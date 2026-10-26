using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.Platform;

// On Windows only the launch the person just started - a click, a file, a browser's link - may bring a window
// forward, and it may let another process do so: the window's, which serves the pipe. Here rather than in a
// Windows host, as both need it.
[SupportedOSPlatform("windows")]
public sealed class WindowsForegroundHandOff : IForegroundHandOff
{
    public void HandTo(NamedPipeClientStream window)
    {
        // a launch with no right of its own has none to hand on, and the window stays where it is
        if (!GetNamedPipeServerProcessId(window.SafePipeHandle, out var processId) ||
            !AllowSetForegroundWindow(processId))
            VhLogger.Instance.LogDebug("The window may not come forward for this launch. Error: {Error}",
                Marshal.GetLastPInvokeError());
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
