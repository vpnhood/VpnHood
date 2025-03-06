using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VpnHood.Core.VpnAdapters.AndroidTun.LinuxNative;

[StructLayout(LayoutKind.Sequential)]
[SuppressMessage("ReSharper", "InconsistentNaming")]
[SuppressMessage("ReSharper", "IdentifierTypo")]
internal struct PollFD
{
    public int fd;
    public short events;
    public short revents;
}