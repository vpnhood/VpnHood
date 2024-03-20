namespace VpnHood.AccessServer.Report.Views;

public class TrafficUsage
{
    public long SentTraffic { get; set; }
    public long ReceivedTraffic { get; set; }
    public DateTime LastUsedTime { get; set; }
}