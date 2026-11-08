namespace VpnHood.Net.PacketTransports;

/// <summary>
/// Provides the settings of a packet transport's send queue.
/// </summary>
public class PacketTransportOptions
{
    /// <summary>The queue's capacity when <see cref="QueueCapacity"/> is not set: 255 packets.</summary>
    public const int DefaultQueueCapacity = 255;

    /// <summary>
    /// Gets the number of packets the send queue holds, or <see langword="null"/> for
    /// <see cref="DefaultQueueCapacity"/>.
    /// </summary>
    public int? QueueCapacity { get; init; }

    /// <summary>
    /// Gets a value indicating whether the transport disposes each packet once sent. Set it to
    /// <see langword="false"/> for a transport that hands its packets on, whose new owner disposes them.
    /// </summary>
    /// <remarks>
    /// A packet the transport drops or refuses, it disposes either way; one a send fails on too, except in a
    /// passthrough transport without this option, whose derived class disposes it.
    /// </remarks>
    public required bool AutoDisposePackets { get; init; }

    /// <summary>
    /// Gets a value indicating whether a send to a full queue waits for room, up to <see cref="BlockingTimeout"/>.
    /// When <see langword="false"/>, a full queue drops its oldest packet instead.
    /// </summary>
    /// <remarks>
    /// A waiting <see cref="IPacketTransport.SendPacketQueued"/> holds its thread; the async send waits without
    /// one, holding its packet outside the queue. Disposing the transport releases the waits. Never send from the
    /// transport's own send method, which the queue waits on: that send would wait out the timeout, or forever without
    /// one.
    /// </remarks>
    public required bool Blocking { get; init; }

    /// <summary>
    /// Gets how long a blocking send waits for room before it drops its packet, or <see langword="null"/> to wait
    /// until there is room or the transport is disposed.
    /// </summary>
    public TimeSpan? BlockingTimeout { get; init; }
}
