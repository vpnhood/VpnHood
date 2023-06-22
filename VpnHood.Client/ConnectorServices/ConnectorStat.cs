namespace VpnHood.Client.ConnectorServices;

public class ConnectorStat
{
    public int FreeConnectionCount { get; internal set; }
    public int ReusedConnectionFailedCount { get; internal set; }
    public int ReusedConnectionSucceededCount { get; internal set; }
    public int CreatedConnectionCount { get; internal set; }
}