using System.Globalization;

namespace VpnHood.AccessServer.Dtos.Regions;

public class Region
{
    public required int RegionId { get; set; }
    public required string? RegionName { get; set; }
    public required string CountryCode { get; set; }
    public string DisplayName
    {
        get
        {
            try
            {
                var regionInfo = new RegionInfo(CountryCode);
                return !string.IsNullOrWhiteSpace(RegionName) ? RegionName : regionInfo.EnglishName;
            }
            catch
            {
                return CountryCode;
            }
        }
    }
}