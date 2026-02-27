using VpnHood.Core.Common.Tokens;

namespace VpnHood.AppLib.Dtos;

public class AppServerLocationInfo : ServerLocationInfo
{
    public string TranslatedCountryName => VpnHoodApp.Instance.Services.LocationService
        .TryGetCountryInfo(CountryCode)?.TranslatedName ?? CountryName;
    public static AppServerLocationInfo FromInfo(ServerLocationInfo serverLocationInfo)
    {
        return new AppServerLocationInfo {
            CountryCode = serverLocationInfo.CountryCode,
            RegionName = serverLocationInfo.RegionName,
            Tags = serverLocationInfo.Tags,
        };
    }
}