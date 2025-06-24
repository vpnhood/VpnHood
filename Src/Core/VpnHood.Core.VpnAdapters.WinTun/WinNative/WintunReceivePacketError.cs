namespace VpnHood.Core.VpnAdapters.WinTun.WinNative;

internal enum WintunReceivePacketError
{
    HandleEof = 0x26,
    NoMoreItems = 0x103,
    InvalidData = 0xd
}