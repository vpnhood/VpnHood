using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Net.VpnAdapters.WinTun;

/// <summary>Represents the settings of a <see cref="WinTunVpnAdapter"/>.</summary>
public class WinTunVpnAdapterSettings : VpnAdapterSettings
{
    /// <summary>
    /// Gets or initializes the size of each of the session's two rings, the one for each direction, in bytes: a power
    /// of two from <see cref="WinTunVpnAdapter.MinRingCapacity"/> to <see cref="WinTunVpnAdapter.MaxRingCapacity"/>.
    /// The default is 4 MiB.
    /// </summary>
    /// <remarks>A size WinTun refuses fails the start, when the adapter opens its session.</remarks>
    public int RingCapacity { get; init; } = 0x400000;

    /// <summary>Initializes a new instance of the <see cref="WinTunVpnAdapterSettings"/> class.</summary>
    public WinTunVpnAdapterSettings()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WinTunVpnAdapterSettings"/> class with the optional settings of the
    /// host's, and this adapter's own at their defaults; the required ones are given in the object initializer.
    /// </summary>
    /// <param name="settings">The settings whose optional ones are copied.</param>
    public WinTunVpnAdapterSettings(VpnAdapterSettings settings) : base(settings)
    {
    }
}