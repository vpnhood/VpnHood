using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.Repos.Views;

public class AccessPointView
{
    public required Guid ServerFarmId { get; init; }
    public required Guid ServerId { get; init; }
    public required string ServerName { get; init; }
    public required AccessPointModel[] AccessPoints { get; init; }
}