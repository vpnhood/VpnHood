namespace VpnHood.AccessServer.Dtos.ServerFarms;

public class ServerFarmData
{
    public required ServerFarm ServerFarm { get; init; }
    public required IEnumerable<AccessPointView> AccessPoints { get; init; }
    public ServerFarmSummary? Summary { get; init; }
}