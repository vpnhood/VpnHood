namespace VpnHood.AccessServer.Dtos.ServerFarmDtos;

public class ServerFarmData
{
    public required ServerFarm ServerFarm { get; init; }
    public ServerFarmSummary? Summary { get; init; }
}
