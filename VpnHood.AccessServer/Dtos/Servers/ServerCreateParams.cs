namespace VpnHood.AccessServer.Dtos.Servers;

public class ServerCreateParams
{
    public string? ServerName { get; init; }
    public required Guid ServerFarmId { get; init; }
    public AccessPoint[]? AccessPoints { get; init; }
}