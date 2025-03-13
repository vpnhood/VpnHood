namespace VpnHood.Core.Client.ConnectorServices;

public abstract class ClientConnectorStat
{
    public abstract int FreeConnectionCount { get; }
    public int ReusedConnectionFailedCount { get; internal set; }
    public int ReusedConnectionSucceededCount { get; internal set; }
    public int CreatedConnectionCount { get; internal set; }
    public int RequestCount { get; internal set; }
}