using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.VpnAdapters.WinDivert;

public class WinDivertVpnAdapterSettings : VpnAdapterSettings
{
    public WinDivertVpnAdapterSettings()
    {
        base.AutoMetric = false;
    }

    public new bool AutoMetric => false;
    public bool ExcludeLocalNetwork { get; set; } = true;
}
