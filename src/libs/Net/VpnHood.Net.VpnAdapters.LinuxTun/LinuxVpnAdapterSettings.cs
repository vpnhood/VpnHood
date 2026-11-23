using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Net.VpnAdapters.LinuxTun;

/// <summary>
/// Provides the settings a <see cref="LinuxTunVpnAdapter"/> is made with.
/// </summary>
/// <remarks>
/// <see cref="VpnAdapterSettings.AutoMetric"/> has no effect here: the adapter leaves the probe networks out of its
/// routes, so they never cover every address of an IP version.
/// </remarks>
public class LinuxVpnAdapterSettings : VpnAdapterSettings
{
    /// <summary>Initializes a new instance of the <see cref="LinuxVpnAdapterSettings"/> class.</summary>
    public LinuxVpnAdapterSettings()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxVpnAdapterSettings"/> class with the optional settings of the
    /// host's; the required ones are given in the object initializer.
    /// </summary>
    /// <param name="settings">The settings whose optional ones are copied.</param>
    public LinuxVpnAdapterSettings(VpnAdapterSettings settings) : base(settings)
    {
    }
}
