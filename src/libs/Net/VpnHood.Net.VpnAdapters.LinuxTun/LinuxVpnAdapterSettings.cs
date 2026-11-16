using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Net.VpnAdapters.LinuxTun;

/// <summary>
/// Provides the settings a <see cref="LinuxTunVpnAdapter"/> is made with.
/// </summary>
/// <remarks>
/// <see cref="VpnAdapterSettings.AutoMetric"/> has no effect here: the adapter leaves the probe networks out of its
/// routes, so they never cover every address of an IP version.
/// </remarks>
public class LinuxVpnAdapterSettings : VpnAdapterSettings;
