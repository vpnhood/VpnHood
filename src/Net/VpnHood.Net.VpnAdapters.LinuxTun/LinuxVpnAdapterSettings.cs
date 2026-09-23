using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Net.VpnAdapters.LinuxTun;

public class LinuxVpnAdapterSettings : VpnAdapterSettings
{
    public LinuxVpnAdapterSettings()
    {
        base.AutoMetric = false;
    }

    public new bool AutoMetric => false;
}