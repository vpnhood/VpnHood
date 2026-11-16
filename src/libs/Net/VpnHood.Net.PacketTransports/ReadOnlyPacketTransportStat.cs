namespace VpnHood.Net.PacketTransports;

/// <summary>
/// Provides a read-only view of a transport's counters, which shows their current values.
/// </summary>
/// <param name="stat">The counters to show.</param>
public class ReadOnlyPacketTransportStat(PacketTransportStat stat)
{
    /// <summary>Gets the number of packets sent.</summary>
    public long SentPackets => stat.SentPackets;

    /// <summary>Gets the number of packets received.</summary>
    public long ReceivedPackets => stat.ReceivedPackets;

    /// <inheritdoc cref="PacketTransportStat.DroppedPackets"/>
    public long DroppedPackets => stat.DroppedPackets;

    /// <summary>Gets the number of bytes sent, IP headers included.</summary>
    public long SentBytes => stat.SentBytes;

    /// <summary>Gets the number of bytes received, IP headers included.</summary>
    public long ReceivedBytes => stat.ReceivedBytes;

    /// <summary>Gets when the counters were made, in UTC.</summary>
    public DateTime CreatedTime => stat.CreatedTime;

    /// <summary>
    /// Gets when the last send of a batch began, whether it failed or not, in UTC; when the counters were made, until
    /// then.
    /// </summary>
    public DateTime LastSentTime => stat.LastSentTime;

    /// <summary>Gets when a packet was last received, in UTC; when the counters were made, until then.</summary>
    public DateTime LastReceivedTime => stat.LastReceivedTime;

    /// <inheritdoc cref="PacketTransportStat.LastActivityTime"/>
    public DateTime LastActivityTime => stat.LastActivityTime;
}
