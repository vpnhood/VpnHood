namespace VpnHood.AccessServer.Dtos.ServerFarmDto;

public class ServerFarmData
{
    public required ServerFarm ServerFarm { get; init; }
    public ServerFarmSummary? Summary { get; init; }
}
