namespace VpnHood.AccessServer.Dtos;

public class ServerFarm
{
    public Guid ServerFarmId { get; init; }
    public Guid ServerProfileId { get; init; }
    public Guid CertificateId { get; init; }
    public required string ServerFarmName { get; init; }
    public required string ServerProfileName { get; init; }
    public required bool UseHostName { get; init; }
    public required Uri? TokenUrl { get; init; }
    public required byte[] Secret { get; init; }
    public required DateTime CreatedTime { get; init; }
    public required bool PushTokenToClient { get; init; }
    public required  bool UseTokenV4 { get; init; }
}

