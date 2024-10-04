using VpnHood.AccessServer.Dtos.Certificates;

namespace VpnHood.AccessServer.Dtos.ServerFarms;

public class ServerFarmData
{
    public required ServerFarm ServerFarm { get; init; }
    public required Certificate[]? Certificates { get; init; }
    public required IEnumerable<AccessPointView> AccessPoints { get; init; }
    public ServerFarmSummary? Summary { get; init; }
    public string[]? FarmTokenRepoNames { get; set; }
}