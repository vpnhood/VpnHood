namespace VpnHood.AccessServer.Dtos;

public class Location
{
    public required string CountryCode { get; set; }
    public required string? RegionName { get; set; }
    public required string? CityName { get; set; }
}