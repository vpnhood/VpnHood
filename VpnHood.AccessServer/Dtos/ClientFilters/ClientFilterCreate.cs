namespace VpnHood.AccessServer.Dtos.ClientFilters;

public class ClientFilterCreate
{
    public required string ClientFilterName { get; init; }
    public required string Filter { get; init; }
}