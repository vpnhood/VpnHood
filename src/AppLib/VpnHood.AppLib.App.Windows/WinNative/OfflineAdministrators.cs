using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace VpnHood.AppLib.App.Windows.WinNative;

// Whether a SID is a member of Administrators, read with nobody signed in as that person: Authz
// gathers the groups a sign-in would give, nested ones included. Throws when it cannot read them.
internal static class OfflineAdministrators
{
    private const uint AuthzRmFlagNoAudit = 0x1;
    private const int AuthzContextInfoGroupsSids = 2;

    public static bool IsMember(string sid)
    {
        var userSid = new SecurityIdentifier(sid);
        var sidBytes = new byte[userSid.BinaryLength];
        userSid.GetBinaryForm(sidBytes, 0);

        if (!AuthzInitializeResourceManager(AuthzRmFlagNoAudit, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, null,
                out var resourceManager))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Authz could not start.");

        try {
            if (!AuthzInitializeContextFromSid(0, sidBytes, resourceManager, IntPtr.Zero, 0, IntPtr.Zero,
                    out var context))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not read the groups of {sid}.");

            try {
                return HasAdministrators(context);
            }
            finally {
                AuthzFreeContext(context);
            }
        }
        finally {
            AuthzFreeResourceManager(resourceManager);
        }
    }

    // The groups come as TOKEN_GROUPS: a count, then SID_AND_ATTRIBUTES at pointer alignment - a SID
    // pointer and its attributes, two pointers wide.
    private static bool HasAdministrators(IntPtr context)
    {
        AuthzGetInformationFromContext(context, AuthzContextInfoGroupsSids, 0, out var size, IntPtr.Zero);
        var buffer = Marshal.AllocHGlobal(size);
        try {
            if (!AuthzGetInformationFromContext(context, AuthzContextInfoGroupsSids, size, out _, buffer))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the groups Authz gathered.");

            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var count = Marshal.ReadInt32(buffer);
            var groups = buffer + IntPtr.Size;
            for (var i = 0; i < count; i++) {
                var sid = Marshal.ReadIntPtr(groups, i * 2 * IntPtr.Size);
                if (sid != IntPtr.Zero && new SecurityIdentifier(sid) == administrators)
                    return true;
            }

            return false;
        }
        finally {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("authz.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AuthzInitializeResourceManager(uint flags, IntPtr accessCheck,
        IntPtr computeDynamicGroups, IntPtr freeDynamicGroups, string? name, out IntPtr resourceManager);

    [DllImport("authz.dll", SetLastError = true)]
    private static extern bool AuthzFreeResourceManager(IntPtr resourceManager);

    // identifier: a LUID, by value, which a long matches in size and passing
    [DllImport("authz.dll", SetLastError = true)]
    private static extern bool AuthzInitializeContextFromSid(uint flags, byte[] userSid, IntPtr resourceManager,
        IntPtr expirationTime, long identifier, IntPtr dynamicGroupArgs, out IntPtr clientContext);

    [DllImport("authz.dll", SetLastError = true)]
    private static extern bool AuthzGetInformationFromContext(IntPtr clientContext, int infoClass, int bufferSize,
        out int sizeRequired, IntPtr buffer);

    [DllImport("authz.dll", SetLastError = true)]
    private static extern bool AuthzFreeContext(IntPtr clientContext);
}
