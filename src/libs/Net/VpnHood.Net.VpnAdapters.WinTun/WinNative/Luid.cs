using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VpnHood.Net.VpnAdapters.WinTun.WinNative;

[StructLayout(LayoutKind.Sequential)]
[SuppressMessage("ReSharper", "IdentifierTypo")]
internal struct Luid
{
    public uint LowPart;
    public int HighPart;
}