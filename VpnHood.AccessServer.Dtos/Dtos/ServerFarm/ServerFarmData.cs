namespace VpnHood.AccessServer.Dtos;

public class ServerFarmData
{
    public required ServerFarm ServerFarm { get; init; }
    public required Certificate Certificate { get; init; }
    public required IEnumerable<ServerFarmAccessPoint> AccessPoints { get; init; }
    public ServerFarmSummary? Summary { get; init; }
}