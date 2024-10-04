using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Dtos;

public class Location
{
    public required string CountryCode { get; set; }
    public required string? RegionName { get; set; }
    public required string? CityName { get; set; }
    public string CountryName => VhUtil.TryGetCountryName(CountryCode) ?? CountryCode;
    public string DisplayName {
        get {
            var name = CountryName;
            var region = $"{RegionName}/{CityName}".Trim('/');
            if (!string.IsNullOrWhiteSpace(region))
                name += $" ({region})";
            return name;
        }
    }
}