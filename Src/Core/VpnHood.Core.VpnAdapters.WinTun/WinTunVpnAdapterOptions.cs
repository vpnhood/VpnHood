using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VpnHood.Core.VpnAdapters.WinTun;

public class WinTunVpnAdapterOptions
{
    public required string AdapterName { get; init; }
    public ILogger Logger { get; init; } = NullLogger<WinTunVpnAdapter>.Instance;

    /// <summary>
    /// Capacity: Rings capacity. Must be between MinRingCapacity and MaxRingCapacity (incl.) Must be a power of two.
    /// </summary>
    public int RingCapacity { get; init; } = 0x400000; // 4MB (default)

    public int MaxPacketCount { get; init; } = 255;
    public TimeSpan MaxPacketSendDelay { get; init; } = TimeSpan.FromMilliseconds(500);
}