using VpnHood.AppLib.Services;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.AppLib.Dtos;

public class AppServerLocationInfo : ServerLocationInfo
{
    public bool HasMultipleRegions { get; init; }

    public string TranslatedCountryName {
        get {
            if (CountryCode == AutoCountryCode)
                return CountryName;

            return AppCountryInfo.TryGet(CountryCode)?.TranslatedName ?? CountryName;
        }
    }

    public static AppServerLocationInfo FromInfo(
        ServerLocationInfo serverLocationInfo,
        bool hasMultipleRegions = false)
    {
        return new AppServerLocationInfo {
            CountryCode = serverLocationInfo.CountryCode,
            RegionName = serverLocationInfo.RegionName,
            Tags = serverLocationInfo.Tags,
            HasMultipleRegions = hasMultipleRegions
        };
    }
}