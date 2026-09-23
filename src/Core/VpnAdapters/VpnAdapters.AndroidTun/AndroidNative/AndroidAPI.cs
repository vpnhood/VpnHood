using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
// ReSharper disable CommentTypo

namespace VpnHood.Core.VpnAdapters.AndroidTun.AndroidNative;

// Direct libc (bionic) calls for the packet hot path. The Java stream/Os bindings marshal the
// whole managed buffer to a fresh Java byte[] on every call (copying it in both directions), while
// a P/Invoke only pins the buffer. SetLastError makes Marshal.GetLastWin32Error return errno,
// unlike the Java bindings which report errors by throwing ErrnoException.
[SuppressMessage("ReSharper", "IdentifierTypo")]
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal static class AndroidAPI
{
    public const int FGetfl = 3; // F_GETFL
    public const int FSetfl = 4; // F_SETFL
    public const int ONonblock = 0x800; // O_NONBLOCK

    [DllImport("libc", SetLastError = true)]
    public static extern int fcntl(int fd, int cmd, int arg);

    [DllImport("libc", SetLastError = true)]
    public static extern int poll([In] [Out] StructPollfd[] fds, nuint nfds, int timeout);

    [DllImport("libc", SetLastError = true)]
    public static extern nint read(int fd, [In] [Out] byte[] buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    public static extern nint write(int fd, ref byte buffer, nuint count);
}
