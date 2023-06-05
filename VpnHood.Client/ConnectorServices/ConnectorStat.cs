namespace VpnHood.Client.ConnectorServices;

public class ConnectorStat
{
    public int ReusedConnectionCount { get; internal set; }
    public int CreatedConnectionCount { get; internal set; }
}