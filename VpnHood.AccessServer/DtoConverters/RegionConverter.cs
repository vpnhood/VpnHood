using VpnHood.AccessServer.Dtos.Regions;
using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class RegionConverter
{
    public static Region ToDto(this RegionModel region)
    {
        return new Region
        {
            RegionId = region.RegionId,
            RegionName = region.RegionName,
            CountryCode = region.CountryCode
        };
    }

}