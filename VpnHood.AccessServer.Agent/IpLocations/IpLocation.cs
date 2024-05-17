namespace VpnHood.AccessServer.Agent.IpLocations;

public class IpLocation
{
    public required string CountryName { get; init; }
    public required string CountryCode { get; init; }
    public required string? RegionName { get; init; }
    public required string? RegionCode { get; init; }
    public required string? CityName { get; init; }
    public required string? CityCode { get; init; }
    public required string? ContinentCode { get; init; }
}