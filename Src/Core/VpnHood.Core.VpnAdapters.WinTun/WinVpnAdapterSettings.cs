using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.VpnAdapters.WinTun;

public class WinVpnAdapterSettings : VpnAdapterSettings
{
    /// <summary>
    /// Capacity: Rings capacity. Must be between MinRingCapacity and MaxRingCapacity (incl.) Must be a power of two.
    /// </summary>
    public int RingCapacity { get; init; } = 0x400000; // 4MB (default)
}