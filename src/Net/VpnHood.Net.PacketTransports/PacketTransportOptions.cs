namespace VpnHood.Net.PacketTransports;

public class PacketTransportOptions
{
    public const int DefaultQueueCapacity = 255;
    public int? QueueCapacity { get; init; }
    public required bool AutoDisposePackets { get; init; }
    // A full queue makes a send wait for room, with no timeout; disposing the transport releases it. Each
    // waiting send holds its thread, so send from a thread of your own, and never from the transport's own
    // SendPacketsAsync, which its queue waits on. A waiting async send holds its packet outside the queue.
    // Without it, a full queue drops its oldest packet
    public required bool Blocking { get; init; }
}