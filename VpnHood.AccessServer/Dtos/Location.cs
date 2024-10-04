using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Dtos;

public class Location
{
    public string CountryName => VhUtil.TryGetCountryName(CountryCode) ?? CountryCode;
    public required string CountryCode { get; set; }
    public required string? RegionName { get; set; }
    public required string? CityName { get; set; }
}