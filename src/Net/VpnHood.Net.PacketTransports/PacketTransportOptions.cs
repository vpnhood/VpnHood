namespace VpnHood.Net.PacketTransports;

public class PacketTransportOptions
{
    public const int DefaultQueueCapacity = 255;
    public int? QueueCapacity { get; init; }

    // Disposes a packet once sent; off for a transport that hands its packets on, whose new owner disposes them.
    // A packet the transport does not deliver it disposes either way
    public required bool AutoDisposePackets { get; init; }

    // A full queue makes a send wait for room, up to BlockingTimeout; disposing the transport releases it. By
    // design of a blocking send, a waiting send holds its thread (the async one waits without), and a waiting
    // async send holds its packet outside the queue. Never send from the transport's own SendPacketsAsync,
    // which the queue waits on: that send would wait out the timeout, or forever without one. Without it, a
    // full queue drops its oldest packet
    public required bool Blocking { get; init; }

    // how long a blocking send waits for room before it drops its packet; none waits for room or disposal
    public TimeSpan? BlockingTimeout { get; init; }
}