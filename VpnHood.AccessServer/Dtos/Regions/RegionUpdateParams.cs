using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Dtos.Regions;

public class RegionUpdateParams
{
    public Patch<string?>? RegionName { get; set; }
    public Patch<string>? CountryCode { get; set; }
}