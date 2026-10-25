using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

// ReSharper disable InconsistentNaming
namespace VpnHood.AppUi.Hosting.Desktop.WindowsTun;

// The windows and trays an update's setup closed (install-client-close.ps1 lists them in the install's
// relaunch.json), opened again by the new service, each in the session it was in and as its person: the service is
// SYSTEM, in no person's session, so the window's launcher starts with the session's own token (WTSQueryUserToken),
// unelevated, as a click would start it. A list from longer ago than a setup's run is one a service of an earlier
// release left unread, and goes unread; a session whose person has signed out since is passed over. What cannot be
// opened is a warning: the person opens it again.
internal static class WindowsUiRelaunch
{
    private const string ListFileName = "relaunch.json";
    private const uint CreateUnicodeEnvironment = 0x400;
    private static readonly TimeSpan ListMaxAge = TimeSpan.FromMinutes(10);

    public static void Run(WindowsDesktopPaths paths)
    {
        if (paths.InstallRootPath is not { } installRootPath)
            return;

        var listPath = Path.Combine(installRootPath, ListFileName);
        if (!File.Exists(listPath))
            return;

        try {
            var isRecent = DateTime.UtcNow - File.GetLastWriteTimeUtc(listPath) < ListMaxAge;
            var uis = JsonSerializer.Deserialize<ClosedUi[]>(File.ReadAllText(listPath)) ?? [];
            File.Delete(listPath);
            if (!isRecent)
                return;

            // one window per person and session, which the UI keeps itself
            foreach (var ui in uis.DistinctBy(x => x.SessionId))
                TryOpen(paths.ExecutablePath, ui);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not open again the windows an update closed.");
        }
    }

    private static void TryOpen(string launcherPath, ClosedUi ui)
    {
        var uiName = ui.IsTray ? "tray" : "window";
        if (!WTSQueryUserToken((uint)ui.SessionId, out var token)) {
            VhLogger.Instance.LogInformation("The {Ui} of session {SessionId} is not opened again: nobody is signed in there.",
                uiName, ui.SessionId);
            return;
        }

        var environment = IntPtr.Zero;
        try {
            if (!CreateEnvironmentBlock(out environment, token, inherit: false))
                throw new Win32Exception();

            var startupInfo = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>(), lpDesktop = @"winsta0\default" };
            var commandLine = new StringBuilder($"\"{launcherPath}\" ui{(ui.IsTray ? " --tray" : "")}");
            if (!CreateProcessAsUser(token, launcherPath, commandLine, IntPtr.Zero, IntPtr.Zero, inheritHandles: false,
                    CreateUnicodeEnvironment, environment, Path.GetDirectoryName(launcherPath), ref startupInfo,
                    out var processInformation))
                throw new Win32Exception();

            CloseHandle(processInformation.hThread);
            CloseHandle(processInformation.hProcess);
            VhLogger.Instance.LogInformation("Opened the {Ui} of session {SessionId} again, which an update closed.",
                uiName, ui.SessionId);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not open the {Ui} of session {SessionId} again.", uiName, ui.SessionId);
        }
        finally {
            if (environment != IntPtr.Zero)
                DestroyEnvironmentBlock(environment);
            CloseHandle(token);
        }
    }

    // as install-client-close.ps1 writes it
    private sealed class ClosedUi
    {
        public int SessionId { get; init; }
        public bool IsTray { get; init; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token,
        [MarshalAs(UnmanagedType.Bool)] bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    // the command line in a buffer of its own, which CreateProcess may write to
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateProcessAsUserW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUser(IntPtr token, string applicationName, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags, IntPtr environment, string? currentDirectory, ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
