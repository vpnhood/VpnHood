using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VpnHood.Core.VpnAdapters.AndroidTun.AndroidNative;

[StructLayout(LayoutKind.Sequential)]
[SuppressMessage("ReSharper", "IdentifierTypo")]
internal struct StructPollfd
{
    public int Fd;
    public short Events;
    public short Revents;
}
