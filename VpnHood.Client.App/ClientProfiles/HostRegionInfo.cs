using System.Globalization;
using VpnHood.Common;

namespace VpnHood.Client.App.ClientProfiles;

public class HostRegionInfo(HostRegion region)
{
    public string RegionId { get; } = region.CountryCode;
    public string RegionName { get; } = string.IsNullOrWhiteSpace(region.RegionName) ? GetRegionName(region.CountryCode) : region.RegionName;
    public string CountryCode { get;  } = region.CountryCode;

    private static string GetRegionName(string regionCode)
    {
        try
        {
            var regionInfo = new RegionInfo(regionCode);
            return regionInfo.EnglishName;
        }
        catch (Exception)
        {
            return regionCode;
        }
    }
}