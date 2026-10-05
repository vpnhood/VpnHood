using System.Runtime.InteropServices;

namespace VpnHood.AppUi.Hosting.Desktop.Linux;

// Group names to ids through the system's own lookup (nsswitch), so a directory's group counts too.
internal static class LinuxGroups
{
    // getgrnam is not reentrant, so the answers are read at once, from one caller, before any
    // check runs; a group made later is seen at the service's next start.
    public static IReadOnlySet<int> Resolve(IEnumerable<string> names)
    {
        var ids = new HashSet<int>();
        foreach (var name in names) {
            var group = getgrnam(name);
            if (group == IntPtr.Zero)
                continue;

            // struct group { char* gr_name; char* gr_passwd; gid_t gr_gid; char** gr_mem; }
            ids.Add(Marshal.ReadInt32(group, 2 * IntPtr.Size));
        }

        return ids;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr getgrnam(string name);
}
