using System.Globalization;
using VpnHood.AppLib.Dtos;
using Nager.Country;
using Nager.Country.Translation;

namespace VpnHood.AppLib.Services;

internal class CountryInfoService
{
    private readonly CountryProvider _countryProvider = new();
    private readonly TranslationProvider _translationProvider = new();

    public CountryInfo GetCountryInfo(string countryCode, CultureInfo cultureInfo)
    {
        var countryInfo = _countryProvider.GetCountry(countryCode);
        var translatedName = _translationProvider.GetCountryTranslatedName(countryInfo.Alpha2Code, cultureInfo);
        return new CountryInfo {
            EnglishName = countryInfo.CommonName,
            TranslatedName = translatedName ?? countryInfo.CommonName,
            CountryCode = countryCode.ToUpper()
        };
    }
}