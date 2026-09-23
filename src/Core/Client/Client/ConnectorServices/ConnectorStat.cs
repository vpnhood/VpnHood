namespace VpnHood.Core.Client.ConnectorServices;

public class ConnectorStat(Func<int> freeConnectionCountFunc)
{
    public int FreeConnectionCount => freeConnectionCountFunc();
    public int ReusedConnectionFailedCount { get; internal set; }
    public int ReusedConnectionSucceededCount { get; internal set; }
    public int CreatedConnectionCount { get; internal set; }
    public int RequestCount { get; internal set; }
}
