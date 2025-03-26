using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VpnHood.Core.VpnAdapters.LinuxTun.LinuxNative;

[SuppressMessage("ReSharper", "IdentifierTypo")]
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct Ifreq
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
    public string ifr_name; // Interface name
    public short ifr_flags; // Flags (e.g., IFF_TUN)
    
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 22)]
    public byte[] padding;
}