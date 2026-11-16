using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Tunneling.Channels;

/// <summary>Represents the options of a <see cref="StreamPacketChannel"/>.</summary>
public class StreamPacketChannelOptions
{
    internal int? QueueCapacity { get; init; }

    /// <summary>Gets a value indicating whether the channel disposes each packet once sent.</summary>
    public required bool AutoDisposePackets { get; init; }

    /// <summary>
    /// Gets a value indicating whether a send to a full queue waits for room; otherwise, the queue drops its oldest
    /// packet.
    /// </summary>
    public required bool Blocking { get; init; }

    /// <summary>
    /// Gets how long the channel lives before it closes itself, or <see langword="null"/> for as long as its connection.
    /// </summary>
    public required TimeSpan? Lifespan { get; init; }

    /// <summary>Gets the channel's ID.</summary>
    public required string ChannelId { get; init; }

    /// <summary>
    /// Gets the meter that counts the channel's traffic and holds its reads to the session's receive limit, or
    /// <see langword="null"/>.
    /// </summary>
    public TrafficMeter? TrafficMeter { get; init; }

    /// <summary>Gets the connection that carries the channel's packets, which the channel owns.</summary>
    public required IStreamConnection StreamConnection { get; init; }

    /// <summary>
    /// Gets or sets the sizes of the channel's send and receive buffers. The default is
    /// <see cref="TransportDefaults.ConnectionPacketBufferSize"/>.
    /// </summary>
    public TransferBufferSize BufferSize { get; set; } = TransportDefaults.ConnectionPacketBufferSize;

    /// <summary>Gets the time of the request that made the channel, by the client's clock.</summary>
    public required DateTime RequestTime { get; init; }
}
