namespace VpnHood.AccessServer.Dtos.ServerFarm;

public class ServerFarmData
{
    public required ServerFarm ServerFarm { get; init; }
    public required string CertificateCommonName { get; init; }
    public required DateTime CertificateExpirationTime { get; init; }
    public required IEnumerable<ServerFarmAccessPoint> AccessPoints { get; init; }
    public ServerFarmSummary? Summary { get; init; }
}