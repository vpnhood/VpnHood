namespace VpnHood.Net.PacketTransports;

public class ReadOnlyPacketTransportStat(PacketTransportStat stat)
{
    public int SentPackets => stat.SentPackets;
    public int ReceivedPackets => stat.ReceivedPackets;
    public int DroppedPackets => stat.DroppedPackets;
    public long SentBytes => stat.SentBytes;
    public long ReceivedBytes => stat.ReceivedBytes;
    public DateTime CreatedTime => stat.CreatedTime;
    public DateTime LastSentTime => stat.LastSentTime;
    public DateTime LastReceivedTime => stat.LastReceivedTime;
    public DateTime LastActivityTime => stat.LastActivityTime;
}