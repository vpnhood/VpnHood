namespace VpnHood.AccessServer.Persistence.Models;

public class ClientFilterModel
{
    public required int ClientFilterId { get; set; }
    public required Guid ProjectId { get; set; }
    public required string ClientFilterName { get; set; }
    public required string Filter { get; set; }
    public required string? Description { get; set; }

    public virtual ProjectModel? Project { get; set; }
}