using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.Platform;

/// <summary>Represents a Windows session, the console's or a Remote Desktop one, by its ID.</summary>
/// <remarks>
/// Only a launch Windows lets bring a window forward, as the one the person just started there, may let another process
/// do so: the window's, which serves the pipe.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsDesktopSession : IDesktopSession
{
    /// <inheritdoc/>
    /// <remarks>It is the session's ID.</remarks>
    public string Name { get; } = ReadSessionId();
    /// <inheritdoc/>
    public bool IsSandboxed { get; init; }

    /// <inheritdoc/>
    /// <remarks>
    /// It asks Windows to let the window's process come forward; where Windows refuses, as for a launch with no right of its
    /// own, the refusal is logged.
    /// </remarks>
    public void HandForegroundTo(NamedPipeClientStream window)
    {
        // a launch with no right of its own has none to hand on, and the window stays where it is
        if (!GetNamedPipeServerProcessId(window.SafePipeHandle, out var processId) ||
            !AllowSetForegroundWindow(processId))
            VhLogger.Instance.LogDebug("The window may not come forward for this launch. Error: {Error}",
                Marshal.GetLastPInvokeError());
    }

    private static string ReadSessionId()
    {
        using var process = Process.GetCurrentProcess();
        return process.SessionId.ToString(CultureInfo.InvariantCulture);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
