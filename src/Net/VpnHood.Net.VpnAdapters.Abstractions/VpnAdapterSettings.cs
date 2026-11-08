using VpnHood.Net.PacketTransports;

namespace VpnHood.Net.VpnAdapters.Abstractions;

/// <summary>
/// Provides the settings a VPN adapter is made with, which hold for its whole life; what changes from one run to
/// the next is in <see cref="VpnAdapterOptions"/>.
/// </summary>
public class VpnAdapterSettings : PacketTransportOptions
{
    /// <summary>
    /// Gets the name of the adapter's interface.
    /// </summary>
    public required string AdapterName { get; init; }

    /// <summary>
    /// Gets the ID of the app the adapter belongs to, which the adapter writes on its interface where the OS lets
    /// it, such as a Linux tun's alias, or <see langword="null"/> to write none.
    /// </summary>
    /// <remarks>
    /// A name alone says nothing about whose a leftover interface is. Any string will do: an adapter writes what
    /// its OS takes, derived from it where needed.
    /// </remarks>
    public string? AppId { get; init; }

    /// <summary>
    /// Gets the backoff limit for a packet the interface cannot take. The delay starts at 5 milliseconds; after each
    /// refusal, the adapter drops the packet if the delay exceeds this limit, or else doubles the delay, waits, and tries
    /// again. The default, 500 milliseconds, allows waits up to 640 milliseconds, about 1.3 seconds in all, besides the
    /// interface's own write waits.
    /// </summary>
    public TimeSpan MaxPacketSendDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets a value indicating whether the adapter keeps itself started: after I/O errors, or a restart whose
    /// start failed, it starts again after a delay, until its <see cref="IVpnAdapter.Stop"/> or disposal. The
    /// default is <see langword="false"/>, where such a stop raises <see cref="IVpnAdapter.Failed"/> instead.
    /// </summary>
    public bool AutoRestart { get; init; }

    /// <summary>
    /// Gets a value indicating whether routes that cover every address of an IP version are split into its two
    /// halves, which win over the host's default route without changing its metric. The default is
    /// <see langword="true"/>. Where the adapter leaves the probe networks out of its routes, that comes first, and
    /// what remains is routed as it is.
    /// </summary>
    public bool AutoMetric { get; init; } = true;
}
