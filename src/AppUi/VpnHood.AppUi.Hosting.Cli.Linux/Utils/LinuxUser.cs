using System.Globalization;
using System.Runtime.InteropServices;

namespace VpnHood.AppUi.Hosting.Cli.Linux.Utils;

// Who is running this process. Asked in two places - the daemon, which cannot work without root,
// and the systemctl calls, which explain themselves differently to someone who will be prompted
// for a password.
internal static class LinuxUser
{
    private const int PasswdBufferSize = 16 * 1024;

    // struct passwd: seven fields, none wider than a pointer
    private static readonly int PasswdSize = 8 * IntPtr.Size;

    public static int EffectiveUid => (int)geteuid();
    public static bool IsRoot => geteuid() == 0;

    // A name for the log; the id itself when the system knows no such user. The reentrant lookup,
    // since callers are named concurrently.
    public static string NameOf(int uid)
    {
        var passwd = Marshal.AllocHGlobal(PasswdSize);
        var buffer = Marshal.AllocHGlobal(PasswdBufferSize);
        try {
            // struct passwd { char* pw_name; ... }
            var name = getpwuid_r((uint)uid, passwd, buffer, PasswdBufferSize, out var entry) == 0 && entry != IntPtr.Zero
                ? Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(entry))
                : null;
            return name ?? $"uid {uid.ToString(CultureInfo.InvariantCulture)}";
        }
        finally {
            Marshal.FreeHGlobal(buffer);
            Marshal.FreeHGlobal(passwd);
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern uint geteuid();

    [DllImport("libc", SetLastError = true)]
    private static extern int getpwuid_r(uint uid, IntPtr passwd, IntPtr buffer, nuint bufferSize, out IntPtr entry);
}
