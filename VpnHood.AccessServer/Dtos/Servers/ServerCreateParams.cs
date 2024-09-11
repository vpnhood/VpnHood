using VpnHood.AccessServer.Dtos.AccessPoints;

namespace VpnHood.AccessServer.Dtos.Servers;

public class ServerCreateParams
{
    public string? ServerName { get; init; }
    public required Guid ServerFarmId { get; init; }
    public Uri? HostPanelUrl { get; init; }
    public AccessPoint[]? AccessPoints { get; init; }
    public int? Power { get; init; }
}