namespace VpnHood.AccessServer.Dtos.ClientFilters;

public class ClientFilterCreateParams
{
    public required string ClientFilterName { get; init; }
    public required string Filter { get; init; }
    public required string? Description { get; set; }
}