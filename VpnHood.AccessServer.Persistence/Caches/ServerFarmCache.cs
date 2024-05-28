namespace VpnHood.AccessServer.Persistence.Caches;

public class ServerFarmCache
{
    public required Guid ProjectId { get; init; }
    public required Guid ServerFarmId { get; init; }
    public required string ServerFarmName { get; init; }
    public required bool PushTokenToClient { get; init; }
    public required string? TokenJson { get; init; }
}