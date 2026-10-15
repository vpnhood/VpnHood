using System.Diagnostics.CodeAnalysis;

// ReSharper disable CommentTypo

namespace VpnHood.Net.VpnAdapters.LinuxTun.LinuxNative;

[SuppressMessage("ReSharper", "IdentifierTypo")]
internal static class OsConstants
{
    public const int ORdwr = 0x0002; // O_RDWR. Open for read/write
    public const int Eagain = 11; // EAGAIN
    public const int Eintr = 4; // EINTR
    public const int ONonblock = 0x800; // O_NONBLOCK
    public const int OCloexec = 0x80000; // O_CLOEXEC. Not passed to a child process at its exec
    public const int FSetfl = 4; // F_SETFL
    public const int FDupfdCloexec = 1030; // F_DUPFD_CLOEXEC. A copy of a descriptor, with O_CLOEXEC's effect
    public const short Pollin = 0x001; // POLLIN
    public const short Pollout = 0x004; // POLLOUT
    public const int Tunsetiff = 0x400454ca; // TUNSETIFF. ioctl request code for TUN device,
    public const int SolSocket = 1; // SOL_SOCKET. Socket level for socket options
    public const int SoBindtodevice = 25; // SO_BINDTODEVICE. Socket option to bind to a specific device
}