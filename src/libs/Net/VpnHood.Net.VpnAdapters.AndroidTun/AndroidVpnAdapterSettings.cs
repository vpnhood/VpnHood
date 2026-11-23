using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Net.VpnAdapters.AndroidTun;

/// <summary>
/// Represents the settings of an <see cref="AndroidVpnAdapter"/>, which adds none to <see cref="VpnAdapterSettings"/>.
/// </summary>
public class AndroidVpnAdapterSettings : VpnAdapterSettings
{
    /// <summary>Initializes a new instance of the <see cref="AndroidVpnAdapterSettings"/> class.</summary>
    public AndroidVpnAdapterSettings()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AndroidVpnAdapterSettings"/> class with the optional settings of
    /// the host's; the required ones are given in the object initializer.
    /// </summary>
    /// <param name="settings">The settings whose optional ones are copied.</param>
    public AndroidVpnAdapterSettings(VpnAdapterSettings settings) : base(settings)
    {
    }
}
