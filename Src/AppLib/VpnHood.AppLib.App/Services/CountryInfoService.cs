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
        if (!Enum.TryParse<LanguageCode>(cultureInfo.TwoLetterISOLanguageName, out var languageCode))
            languageCode = LanguageCode.EN;

        var countryInfo = _countryProvider.GetCountry(countryCode);
        var translation = _translationProvider.GetCountryTranslation(countryInfo.Alpha2Code);
        var translatedName = translation?.Translations.FirstOrDefault(t => t.LanguageCode == languageCode)?.Name;
        return new CountryInfo {
            EnglishName = translatedName, //countryInfo.CommonName,
            TranslatedName = translatedName ?? countryInfo.CommonName,
            CountryCode = countryCode.ToUpper()
        };
    }
}