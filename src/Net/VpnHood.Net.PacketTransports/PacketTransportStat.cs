using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.PacketTransports;

public class PacketTransportStat
{
    public int SentPackets { get; set; }
    public int ReceivedPackets { get; set; }
    public int DroppedPackets { get; set; }
    public int SentBytes { get; set; }
    public int ReceivedBytes { get; set; }
    public DateTime CreatedTime { get; set; } = FastDateTime.UtcNow;
    public DateTime LastSentTime { get; set; } = FastDateTime.UtcNow;
    public DateTime LastReceivedTime { get; set; } = FastDateTime.UtcNow;
    public DateTime LastActivityTime => LastReceivedTime > LastSentTime ? LastReceivedTime : LastSentTime;
}