using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Net.VpnAdapters.WinDivert;

public class WinDivertVpnAdapterSettings : VpnAdapterSettings
{
    public WinDivertVpnAdapterSettings()
    {
        base.AutoMetric = false;
    }

    public new bool AutoMetric => false;
    public bool ExcludeLocalNetwork { get; set; } = true;
    public bool SimulateDns { get; set; } = true;
}