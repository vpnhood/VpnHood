using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.VpnAdapters.WinDivert;

public class WinDivertVpnAdapterSettings : VpnAdapterSettings
{
    public WinDivertVpnAdapterSettings()
    {
        base.MaxPacketCount = 1;
    }

    public new int MaxPacketCount => 1;
}