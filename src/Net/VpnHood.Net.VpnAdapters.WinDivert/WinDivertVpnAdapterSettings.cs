using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Net.VpnAdapters.WinDivert;

/// <summary>Represents the settings of a <see cref="WinDivertVpnAdapter"/>.</summary>
public class WinDivertVpnAdapterSettings : VpnAdapterSettings
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WinDivertVpnAdapterSettings"/> class, with no automatic metric.
    /// </summary>
    public WinDivertVpnAdapterSettings()
    {
        base.AutoMetric = false;
    }

    /// <summary>
    /// Gets <see langword="false"/>: with no interface, there is no metric. It hides
    /// <see cref="VpnAdapterSettings.AutoMetric"/>, which the constructor sets to <see langword="false"/>.
    /// </summary>
    public new bool AutoMetric => false;

    /// <summary>
    /// Gets or sets whether the capture of the routes' networks leaves out the local networks
    /// (<see cref="VpnHood.Net.Toolkit.Net.IpNetwork.LocalNetworks"/>), whatever the routes; DNS queries to them are
    /// still captured with <see cref="SimulateDns"/>. The default is <see langword="true"/>.
    /// </summary>
    public bool ExcludeLocalNetwork { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the adapter captures every outbound UDP packet to port 53, wherever it goes, but loopback
    /// ones and those of <see cref="WinDivertVpnAdapter.ProtectedTtl"/>, and sends it to one of its own DNS servers of
    /// the packet's IP version instead, if it has one, as the host's DNS settings stay as they are. The default is
    /// <see langword="true"/>.
    /// </summary>
    public bool SimulateDns { get; set; } = true;
}