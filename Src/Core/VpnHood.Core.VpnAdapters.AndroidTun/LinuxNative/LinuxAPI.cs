using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VpnHood.Core.VpnAdapters.AndroidTun.LinuxNative;

[SuppressMessage("ReSharper", "IdentifierTypo")]
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal static class LinuxAPI
{
    public const int EINTR = 4;
    public const int EAGAIN = 11;

    [DllImport("libc", SetLastError = true)]
    public static extern int poll([In, Out] PollFD[] fds, int nfds, int timeout);
}