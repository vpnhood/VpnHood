using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Net.VpnAdapters.WinTun;

/// <summary>Represents the settings of a <see cref="WinTunVpnAdapter"/>.</summary>
public class WinVpnAdapterSettings : VpnAdapterSettings
{
    /// <summary>
    /// Gets or initializes the size of each of the session's two rings, the one for each direction, in bytes: a power
    /// of two from <see cref="WinTunVpnAdapter.MinRingCapacity"/> to <see cref="WinTunVpnAdapter.MaxRingCapacity"/>.
    /// The default is 4 MiB.
    /// </summary>
    /// <remarks>A size WinTun refuses fails the start, when the adapter opens its session.</remarks>
    public int RingCapacity { get; init; } = 0x400000;
}