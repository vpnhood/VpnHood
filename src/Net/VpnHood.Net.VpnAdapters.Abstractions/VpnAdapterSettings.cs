using VpnHood.Net.PacketTransports;

namespace VpnHood.Net.VpnAdapters.Abstractions;

public class VpnAdapterSettings : PacketTransportOptions
{
    public required string AdapterName { get; init; }

    // The app the adapter belongs to, written on it where the OS lets it (a Linux tun's alias): a
    // name alone says nothing about whose a leftover is. Any string: an adapter writes what its OS
    // takes, derived from it where needed. Null writes none.
    public string? AppId { get; init; }

    public TimeSpan MaxPacketSendDelay { get; init; } = TimeSpan.FromMilliseconds(500);
    public bool AutoRestart { get; init; }

    /// <summary>
    /// Automatically adjusts route metrics by splitting routes when all routes are included.
    /// This helps to avoid conflicts and ensures proper prioritization of network routes.
    /// </summary>
    public bool AutoMetric { get; init; } = true;
}
