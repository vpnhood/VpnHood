using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.PacketTransports;

public class PacketTransportStat
{
    private int _droppedPackets;
    public int SentPackets { get; set; }
    public int ReceivedPackets { get; set; }

    // counted by the sender and by a writer whose packet pushed the oldest out of a full queue
    public int DroppedPackets => _droppedPackets;
    public void AddDroppedPacket() => Interlocked.Increment(ref _droppedPackets);
    public int SentBytes { get; set; }
    public int ReceivedBytes { get; set; }
    public DateTime CreatedTime { get; set; } = FastDateTime.UtcNow;
    public DateTime LastSentTime { get; set; } = FastDateTime.UtcNow;
    public DateTime LastReceivedTime { get; set; } = FastDateTime.UtcNow;
    public DateTime LastActivityTime => LastReceivedTime > LastSentTime ? LastReceivedTime : LastSentTime;
}