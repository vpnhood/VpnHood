using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Net.VpnAdapters.IosTun;

/// <summary>
/// Represents the settings of an <see cref="IosVpnAdapter"/>, which adds none to <see cref="VpnAdapterSettings"/>.
/// </summary>
public class IosVpnAdapterSettings : VpnAdapterSettings
{
    /// <summary>Initializes a new instance of the <see cref="IosVpnAdapterSettings"/> class.</summary>
    public IosVpnAdapterSettings()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IosVpnAdapterSettings"/> class with the optional settings of the
    /// host's; the required ones are given in the object initializer.
    /// </summary>
    /// <param name="settings">The settings whose optional ones are copied.</param>
    public IosVpnAdapterSettings(VpnAdapterSettings settings) : base(settings)
    {
    }
}
