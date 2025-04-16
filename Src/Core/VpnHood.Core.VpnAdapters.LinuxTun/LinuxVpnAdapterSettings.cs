using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.VpnAdapters.LinuxTun;

public class LinuxVpnAdapterSettings : VpnAdapterSettings
{
    public LinuxVpnAdapterSettings()
    {
        base.AutoMetric = false;
    }

    public new bool AutoMetric => false;
}
