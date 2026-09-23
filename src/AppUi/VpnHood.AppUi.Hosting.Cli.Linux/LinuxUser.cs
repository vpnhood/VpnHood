using System.Runtime.InteropServices;

namespace VpnHood.AppUi.Hosting.Cli.Linux;

// Who is running this process. Asked in two places - the daemon, which cannot work without root,
// and the systemctl calls, which explain themselves differently to someone who will be prompted
// for a password.
internal static class LinuxUser
{
    public static bool IsRoot => geteuid() == 0;

    [DllImport("libc", SetLastError = true)]
    private static extern uint geteuid();
}
