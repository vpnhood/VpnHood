using System.Globalization;
using VpnHood.AppLib.Dtos;
using Nager.Country;
using Nager.Country.Translation;

namespace VpnHood.AppLib.Services;

internal class CountryInfoService
{
    private readonly Lazy<CountryProvider> _countryProvider = new(() => new CountryProvider());
    private readonly Lazy<TranslationProvider> _translationProvider = new(() => new TranslationProvider());
    private bool _translatorHasError;

    public CountryInfo GetCountryInfo(string countryCode, CultureInfo cultureInfo)
    {
        var countryInfo = _countryProvider.Value.GetCountry(countryCode);
        var translatedName = TryGetTranslatedName(countryInfo.Alpha2Code, cultureInfo);
        return new CountryInfo {
            EnglishName = countryInfo.CommonName,
            TranslatedName = translatedName ?? countryInfo.CommonName,
            CountryCode = countryCode.ToUpper()
        };
    }

    private string? TryGetTranslatedName(Alpha2Code alpha2Code, CultureInfo cultureInfo)
    {
        try {
            return _translatorHasError 
                ? null : _translationProvider.Value.GetCountryTranslatedName(alpha2Code, cultureInfo);
        }
        catch (FileNotFoundException) {
            // Don't let dummy .net error drain the performance. Just disable translation if it happens.
            _translatorHasError = true;
            return null;
        }
    }
}