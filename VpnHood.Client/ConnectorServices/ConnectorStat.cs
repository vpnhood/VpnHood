namespace VpnHood.Client.ConnectorServices;

public class ConnectorStat
{
    public int ReusedConnectionFailedCount { get; internal set; }
    public int ReusedConnectionSucceededCount { get; internal set; }
    public int NewConnectionCount { get; internal set; }
}