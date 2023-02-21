namespace VpnHood.AccessServer.Dtos;

public class ServerFarmData
{
    public required AccessPointGroup ServerFarm {get;set;}
    public required int ServerCount { get; init; }
}
