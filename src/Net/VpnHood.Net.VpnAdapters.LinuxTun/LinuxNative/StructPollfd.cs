using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VpnHood.Net.VpnAdapters.LinuxTun.LinuxNative;

[StructLayout(LayoutKind.Sequential)]
[SuppressMessage("ReSharper", "IdentifierTypo")]
internal struct StructPollfd
{
    public int Fd;
    public short Events;
    public short Revents;
}