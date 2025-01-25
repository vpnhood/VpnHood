using VpnHood.Core.Client.ConnectorServices;

namespace VpnHood.AppLib.Dtos;

public class AppConnectorStat
{
    public required int FreeConnectionCount { get; init; }
    public required int ReusedConnectionFailedCount { get; init; }
    public required int ReusedConnectionSucceededCount { get; init; }
    public required int CreatedConnectionCount { get; init; }
    public required int RequestCount { get; init; }

    public static AppConnectorStat Create(ConnectorStat connectorStat)
    {
        return new AppConnectorStat {
            FreeConnectionCount = connectorStat.FreeConnectionCount,
            ReusedConnectionFailedCount = connectorStat.ReusedConnectionFailedCount,
            ReusedConnectionSucceededCount = connectorStat.ReusedConnectionSucceededCount,
            CreatedConnectionCount = connectorStat.CreatedConnectionCount,
            RequestCount = connectorStat.RequestCount
        };
    }
}