using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VpnHood.Core.Client.Device.Droid.LinuxNative;

[SuppressMessage("ReSharper", "IdentifierTypo")]
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal static class LinuxAPI
{
    public const int EINTR = 4;
    public const int EAGAIN = 11;
    public const int F_SETFL = 4;
    public const int O_NONBLOCK = 0x800;

    public const int IFF_TUN = 0x0001;  // TUN device (Layer 3)
    public const int IFF_NO_PI = 0x1000; // No packet information
    public const int TUNSETIFF = 0x400454ca; // ioctl request code for TUN device
    public const int ORdwr = 0x0002; // Open for read/write
    
    [DllImport("libc", SetLastError = true)]
    public static extern int fcntl(int fd, int cmd, int arg);
    
    [DllImport("libc", SetLastError = true)]
    public static extern int open(string pathname, int flags);
    
    [DllImport("libc", SetLastError = true)]
    public static extern int ioctl(int fd, uint request, ref Ifreq ifr);
    
    [DllImport("libc", SetLastError = true)]
    public static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    public static extern int poll([In, Out] PollFD[] fds, int nfds, int timeout);
    
    [DllImport("libc", SetLastError = true)]
    public static extern int read(int fd, byte[] buffer, int count);

    [DllImport("libc", SetLastError = true)]
    public static extern int write(int fd, byte[] buffer, int count);
    
}