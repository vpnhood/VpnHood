using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.PacketTransports;

/// <summary>
/// Provides the counters of a packet transport, which the transport writes.
/// </summary>
/// <remarks>
/// The packet and byte counts are written without a memory fence, for speed: on a 32-bit platform a reader may see
/// a torn value, and receivers on several threads may lose an increment. Drops are rare, so they are counted
/// exactly. The times are UTC.
/// </remarks>
public class PacketTransportStat
{
    private long _droppedPackets;

    /// <summary>Gets or sets the number of packets sent.</summary>
    public long SentPackets { get; set; }

    /// <summary>Gets or sets the number of packets received.</summary>
    public long ReceivedPackets { get; set; }

    /// <summary>
    /// Gets the number of packets dropped: refused, timed out, failed, pushed out of a full queue, or left in the queue
    /// at disposal. A received packet whose handler throws is disposed without being counted.
    /// </summary>
    public long DroppedPackets => Volatile.Read(ref _droppedPackets);

    /// <summary>Adds one to <see cref="DroppedPackets"/>, safely from any thread.</summary>
    public void AddDroppedPacket() => Interlocked.Increment(ref _droppedPackets);

    /// <summary>Gets or sets the number of bytes sent, IP headers included.</summary>
    public long SentBytes { get; set; }

    /// <summary>Gets or sets the number of bytes received, IP headers included.</summary>
    public long ReceivedBytes { get; set; }

    /// <summary>Gets or sets when the counters were made.</summary>
    public DateTime CreatedTime { get; set; } = FastDateTime.UtcNow;

    /// <summary>
    /// Gets or sets when the last send of a batch began, whether it failed or not; when the counters were made, until
    /// then.
    /// </summary>
    public DateTime LastSentTime { get; set; } = FastDateTime.UtcNow;

    /// <summary>Gets or sets when a packet was last received; when the counters were made, until then.</summary>
    public DateTime LastReceivedTime { get; set; } = FastDateTime.UtcNow;

    /// <summary>Gets the later of <see cref="LastSentTime"/> and <see cref="LastReceivedTime"/>.</summary>
    public DateTime LastActivityTime => LastReceivedTime > LastSentTime ? LastReceivedTime : LastSentTime;
}
