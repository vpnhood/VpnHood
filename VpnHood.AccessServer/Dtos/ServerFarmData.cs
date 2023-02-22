namespace VpnHood.AccessServer.Dtos;

public class ServerFarmData
{
    public required AccessPointGroup ServerFarm { get; init; }
    public ServerFarmSummary? Summary { get; init; }
}
