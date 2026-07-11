using VpnHood.Core.Common.Tokens;

namespace VpnHood.AppLib.Dtos;

public class AppServerLocationInfo : ServerLocationInfo
{
    public string TranslatedCountryName {
        get {
            if (CountryCode == AutoCountryCode)
                return CountryName;

            return VpnHoodApp.Instance.Services.LocationService
                .TryGetCountryInfo(CountryCode)?.TranslatedName ?? CountryName;
        }
    }

    public bool HasMultipleRegions { get; init; }

    public static AppServerLocationInfo FromInfo(ServerLocationInfo serverLocationInfo, bool hasMultipleRegions = false)
    {
        return new AppServerLocationInfo {
            CountryCode = serverLocationInfo.CountryCode,
            RegionName = serverLocationInfo.RegionName,
            Tags = serverLocationInfo.Tags,
            HasMultipleRegions = hasMultipleRegions
        };
    }
}