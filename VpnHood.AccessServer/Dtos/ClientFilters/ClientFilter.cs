namespace VpnHood.AccessServer.Dtos.ClientFilters;

public class ClientFilter
{
    public required string ClientFilterId { get; init; }
    public required string ClientFilterName { get; init; }
    public required string Filter { get; init; }
}