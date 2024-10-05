namespace VpnHood.AccessServer.Persistence.Caches;

public class ClientFilterCache
{
    public required int ClientFilterId { get; init; }
    public required string Filter { get; init; }
}