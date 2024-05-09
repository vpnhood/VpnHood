namespace VpnHood.AccessServer.Dtos.Regions;

public class Region
{
    public required int RegionId { get; set; }
    public required string? RegionName { get; set; }
    public required string CountryCode { get; set; }
}