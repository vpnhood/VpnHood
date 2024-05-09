namespace VpnHood.AccessServer.Persistence.Models;

public class RegionModel
{
    public required int RegionId { get; set; }
    public required string? RegionName { get; set; }
    public required string CountryCode { get; set; }
    public required Guid ProjectId { get; set; }

    public virtual ICollection<ServerModel>? Servers { get; set; }
}