using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VpnHood.Core.VpnAdapters.LinuxTun.LinuxNative;

[StructLayout(LayoutKind.Sequential)]
[SuppressMessage("ReSharper", "IdentifierTypo")]
internal struct StructPollfd
{
    public int Fd;
    public short Events;
    public short Revents;
}