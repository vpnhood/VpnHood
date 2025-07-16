using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VpnHood.Core.VpnAdapters.LinuxTun.LinuxNative;

[SuppressMessage("ReSharper", "IdentifierTypo")]
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal static class LinuxAPI
{
    [DllImport("libc", SetLastError = true)]
    public static extern int fcntl(int fd, int cmd, int arg);
    
    [DllImport("libc", SetLastError = true)]
    public static extern int open(string pathname, int flags);
    
    [DllImport("libc", SetLastError = true)]
    public static extern int ioctl(int fd, uint request, ref Ifreq ifr);
    
    [DllImport("libc", SetLastError = true)]
    public static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    public static extern int poll([In, Out] StructPollfd[] fds, int nfds, int timeout);
    
    [DllImport("libc", SetLastError = true)]
    public static extern int read(int fd, byte[] buffer, int count);

    [DllImport("libc", SetLastError = true)]
    public static extern int write(int fd, byte[] buffer, int count);

    [DllImport("libc", SetLastError = true)]
    public static extern int setsockopt(int sockfd, int level, int optname, byte[] optval, uint optlen);
}