namespace VpnHood.Core.PacketTransports;

public class ReadOnlyPacketTransportStat(PacketTransportStat stat)
{
    public int SentPackets => stat.SentPackets;
    public int ReceivedPackets => stat.ReceivedPackets;
    public int DroppedPackets => stat.DroppedPackets;
    public int SentBytes => stat.SentBytes;
    public int ReceivedBytes => stat.ReceivedBytes;
    public DateTime CreatedTime => stat.CreatedTime;
    public DateTime LastSentTime => stat.LastSentTime;
    public DateTime LastReceivedTime => stat.LastReceivedTime;
    public DateTime LastActivityTime => stat.LastActivityTime;
}