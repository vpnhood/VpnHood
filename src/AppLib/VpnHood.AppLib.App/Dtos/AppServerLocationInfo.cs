using VpnHood.AppLib.Services;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.AppLib.Dtos;

public class AppServerLocationInfo : ServerLocationInfo
{
    public string? TranslatedCountryName { get; init; }
    public bool HasMultipleRegions { get; init; }

    private static string GetTranslatedCountryName(ServerLocationInfo serverLocationInfo, LocationService locationService)
    {
        if (serverLocationInfo.CountryCode == AutoCountryCode)
            return serverLocationInfo.CountryName;

        return locationService
            .TryGetCountryInfo(serverLocationInfo.CountryCode)?.TranslatedName ?? serverLocationInfo.CountryName;
    }

    public static AppServerLocationInfo FromInfo(
        ServerLocationInfo serverLocationInfo,
        bool hasMultipleRegions = false,
        LocationService? locationService = null)
    {
        return new AppServerLocationInfo {
            CountryCode = serverLocationInfo.CountryCode,
            RegionName = serverLocationInfo.RegionName,
            Tags = serverLocationInfo.Tags,
            HasMultipleRegions = hasMultipleRegions,
            TranslatedCountryName = locationService != null
                ? GetTranslatedCountryName(serverLocationInfo, locationService)
                : serverLocationInfo.CountryName
        };
    }
}