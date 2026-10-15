namespace VpnHood.Net.VpnAdapters.LinuxTun.LinuxNative;

[Flags]
public enum InterfaceFlag : ushort
{
    IffTun = 0x0001, // IFF_TUN
    IffNoPi = 0x1000, // IFF_NO_PI
    IffTunExcl = 0x8000 // IFF_TUN_EXCL: refuse an interface that already has the name, rather than attach to it
}