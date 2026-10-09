using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.PacketTransports;

// The packet path counts with plain writes, no fence per packet: on a 32-bit platform a reader may see a torn
// value as a low word wraps, and receivers on several threads may lose an increment. Drops are rare, so they
// are counted exactly
public class PacketTransportStat
{
    private long _droppedPackets;
    public long SentPackets { get; set; }
    public long ReceivedPackets { get; set; }

    // counted by the sender and by a writer whose packet pushed the oldest out of a full queue
    public long DroppedPackets => Volatile.Read(ref _droppedPackets);
    public void AddDroppedPacket() => Interlocked.Increment(ref _droppedPackets);
    public long SentBytes { get; set; }
    public long ReceivedBytes { get; set; }
    public DateTime CreatedTime { get; set; } = FastDateTime.UtcNow;
    public DateTime LastSentTime { get; set; } = FastDateTime.UtcNow;
    public DateTime LastReceivedTime { get; set; } = FastDateTime.UtcNow;
    public DateTime LastActivityTime => LastReceivedTime > LastSentTime ? LastReceivedTime : LastSentTime;
}