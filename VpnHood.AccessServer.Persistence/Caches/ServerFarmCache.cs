namespace VpnHood.AccessServer.Persistence.Caches;

public class ServerFarmCache
{
    public required Guid ProjectId { get; init; }
    public required Guid ServerFarmId { get; init; }
}