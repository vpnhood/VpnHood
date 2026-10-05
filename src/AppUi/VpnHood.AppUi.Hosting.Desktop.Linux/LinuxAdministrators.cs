using System.Runtime.InteropServices;
using VpnHood.AppUi.Hosting.Desktop.Linux.Utils;

namespace VpnHood.AppUi.Hosting.Desktop.Linux;

// Who may use the app on Linux: root, or a member of sudo, wheel or admin, the groups that give sudo
// on the distributions it ships for. Sudo rights given another way, such as a sudoers line for one
// person or for another group, are not seen; such a person uses sudo. The service asks it of every
// caller of its channel (LinuxDaemonChannel), and the window and the commands of themselves first.
internal static class LinuxAdministrators
{
    public static readonly string[] Groups = ["sudo", "wheel", "admin"];

    // whoever runs this process: its user, and its primary and supplementary groups
    public static bool IsCurrentUser()
    {
        if (LinuxUser.IsRoot)
            return true;

        var groupIds = LinuxGroups.Resolve(Groups);
        return groupIds.Contains((int)getegid()) || GetGroups().Any(groupIds.Contains);
    }

    private static int[] GetGroups()
    {
        var count = getgroups(0, null);
        if (count < 0)
            throw new InvalidOperationException($"getgroups failed: errno {Marshal.GetLastPInvokeError()}.");

        var groups = new int[count];
        var read = getgroups(count, groups);
        return read < 0
            ? throw new InvalidOperationException($"getgroups failed: errno {Marshal.GetLastPInvokeError()}.")
            : groups[..read];
    }

    [DllImport("libc", SetLastError = true)]
    private static extern uint getegid();

    [DllImport("libc", SetLastError = true)]
    private static extern int getgroups(int size, [Out] int[]? list);
}
