namespace VpnHood.Core.Packets;

public enum IpEcnField : byte
{
    NonEct = 0, // 00
    Ect1 = 1,   // 01
    Ect0 = 2,   // 10
    Ce = 3      // 11
}