using VpnHood.AccessServer.Dtos.Certificates;

namespace VpnHood.AccessServer.Dtos.ServerFarms;

public class ServerFarm
{
    public Guid ServerFarmId { get; init; }
    public Guid ServerProfileId { get; init; }
    public required string ServerFarmName { get; init; }
    public required string ServerProfileName { get; init; }
    public required bool UseHostName { get; init; }
    public required string? TokenError { get; init; }
    public required byte[] Secret { get; init; }
    public required DateTime CreatedTime { get; init; }
    public required bool PushTokenToClient { get; init; }
    public required int MaxCertificateCount { get; init; }
    public required Certificate? Certificate { get; set; }
}