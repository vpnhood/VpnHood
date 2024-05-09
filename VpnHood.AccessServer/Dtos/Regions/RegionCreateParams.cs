namespace VpnHood.AccessServer.Dtos.Regions;

public class RegionCreateParams
{
    public string? RegionName { get; set; }
    public required string CountryCode { get; set; }
}