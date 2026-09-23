namespace VpnHood.Core.Client.VpnServices.Abstractions;

public class ConnectorStatus
{
    public required int FreeConnectionCount { get; init; }
    public required int ReusedConnectionFailedCount { get; init; }
    public required int ReusedConnectionSucceededCount { get; init; }
    public required int CreatedConnectionCount { get; init; }
    public required int RequestCount { get; init; }
}