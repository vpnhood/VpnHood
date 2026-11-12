using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Net.VpnAdapters.LinuxTun;

/// <summary>
/// Provides the settings a <see cref="LinuxTunVpnAdapter"/> is made with.
/// </summary>
public class LinuxVpnAdapterSettings : VpnAdapterSettings
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxVpnAdapterSettings"/> class, with <see cref="AutoMetric"/>
    /// off.
    /// </summary>
    public LinuxVpnAdapterSettings()
    {
        base.AutoMetric = false;
    }

    /// <summary>
    /// Gets a value indicating whether routes that cover every address of an IP version are split into its two
    /// halves: always <see langword="false"/> here.
    /// </summary>
    public new bool AutoMetric => false;
}
