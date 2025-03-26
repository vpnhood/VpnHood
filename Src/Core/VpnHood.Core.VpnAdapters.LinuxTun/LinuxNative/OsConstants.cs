using System.Diagnostics.CodeAnalysis;
// ReSharper disable CommentTypo

namespace VpnHood.Core.VpnAdapters.LinuxTun.LinuxNative;

[SuppressMessage("ReSharper", "IdentifierTypo")]
internal static class OsConstants
{
    public const int ORdwr =  0x0002; // O_RDWR. Open for read/write
    public const int Eagain = 11; // EAGAIN
    public const int Eintr = 4; // EINTR
    public const int ONonblock = 0x800; // O_NONBLOCK
    public const int FSetfl = 4; // F_SETFL
    public const int Tunsetiff = 0x400454ca; // TUNSETIFF. ioctl request code for TUN device
}