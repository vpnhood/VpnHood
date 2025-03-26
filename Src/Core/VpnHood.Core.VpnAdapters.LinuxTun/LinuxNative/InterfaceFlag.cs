namespace VpnHood.Core.VpnAdapters.LinuxTun.LinuxNative;

[Flags]
public enum InterfaceFlag : ushort
{
    IffTun = 0x0001,  // IFF_TUN
    IffNoPi = 0x1000 // IFF_NO_PI
}