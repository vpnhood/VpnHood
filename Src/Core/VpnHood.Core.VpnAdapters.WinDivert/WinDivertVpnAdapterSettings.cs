using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.VpnAdapters.WinDivert;

public class WinDivertVpnAdapterSettings : TunVpnAdapterSettings
{
    public WinDivertVpnAdapterSettings()
    {
        base.MaxPacketCount = 1;
    }

    public new int MaxPacketCount => 1;
}