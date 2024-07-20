using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class LocationConverter
{
    public static Location ToDto(this LocationModel location)
    {
        return new Location {
            CountryCode = location.CountryCode,
            CountryName = location.CountryName,
            RegionCode = location.RegionCode,
            RegionName = location.RegionName,
            CityCode = location.CityCode,
            CityName = location.CityName
        };
    }
}