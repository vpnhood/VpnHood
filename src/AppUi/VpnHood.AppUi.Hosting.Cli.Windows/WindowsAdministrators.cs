using System.Runtime.InteropServices;
using System.Security.Principal;

namespace VpnHood.AppUi.Hosting.Cli.Windows;

// Whether a token is an administrator's: UAC leaves Administrators deny-only in an unelevated
// window's token, so the group is looked for in the full token Windows links to it too.
internal static class WindowsAdministrators
{
    private const int TokenLinkedToken = 19;

    public static bool IsMember(WindowsIdentity identity)
    {
        if (IsInRole(identity))
            return true;

        if (!TryGetLinkedToken(identity.Token, out var linked))
            return false;

        try {
            using var full = new WindowsIdentity(linked);
            return IsInRole(full);
        }
        finally {
            CloseHandle(linked);
        }
    }

    private static bool IsInRole(WindowsIdentity identity)
    {
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool TryGetLinkedToken(IntPtr token, out IntPtr linked)
    {
        linked = IntPtr.Zero;
        var buffer = Marshal.AllocHGlobal(IntPtr.Size);
        try {
            if (!GetTokenInformation(token, TokenLinkedToken, buffer, IntPtr.Size, out _))
                return false;

            linked = Marshal.ReadIntPtr(buffer);
            return linked != IntPtr.Zero;
        }
        finally {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass,
        IntPtr tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
