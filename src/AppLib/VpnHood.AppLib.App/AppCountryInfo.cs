using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Nager.Country;
using Nager.Country.Translation;
using VpnHood.AppLib.Dtos;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib;

/// <summary>
/// A reliable RegionInfo companion: country names with translations, keyed by ISO alpha-2 code.
/// Encapsulates the country database (Nager) so no other type depends on it, and never throws:
/// unknown or non-ISO codes (e.g. "419", "IV", "001") return null.
/// </summary>
public static class AppCountryInfo
{
    private static readonly CountryProvider CountryProvider = new();
    private static readonly TranslationProvider TranslationProvider = new();
    private static readonly ConcurrentDictionary<(string CountryCode, string CultureName), CountryInfo?> Cache = new();
    private static bool _translatorHasError;

    public static CountryInfo? TryGet(string countryCode, CultureInfo? cultureInfo = null)
    {
        cultureInfo ??= CultureInfo.DefaultThreadCurrentUICulture ?? CultureInfo.CurrentUICulture;
        return Cache.GetOrAdd((countryCode.ToUpperInvariant(), cultureInfo.Name),
            _ => Build(countryCode, cultureInfo));
    }

    private static CountryInfo? Build(string countryCode, CultureInfo cultureInfo)
    {
        try {
            var countryInfo = CountryProvider.GetCountry(countryCode);
            var translatedName = TryGetTranslatedName(countryInfo.Alpha2Code, cultureInfo);
            return new CountryInfo {
                EnglishName = countryInfo.CommonName,
                TranslatedName = translatedName ?? countryInfo.CommonName,
                CountryCode = countryCode.ToUpperInvariant()
            };
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Could not get country info for country code: {CountryCode}", countryCode);
            return null;
        }
    }

    private static string? TryGetTranslatedName(Alpha2Code alpha2Code, CultureInfo cultureInfo)
    {
        try {
            return _translatorHasError
                ? null : TranslationProvider.GetCountryTranslatedName(alpha2Code, cultureInfo);
        }
        catch (FileNotFoundException) {
            // Don't let dummy .net error drain the performance. Just disable translation if it happens.
            _translatorHasError = true;
            return null;
        }
    }

    public static CountryInfo[] GetAll(CultureInfo? cultureInfo = null)
    {
        // region may return 001 for world, but it is not a valid country code, so filter it out,
        // and also filter out any region with numeric name, just in case
        var countryInfos = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(culture => new RegionInfo(culture.Name))
            .Where(region => !string.IsNullOrEmpty(region.Name) && !int.TryParse(region.Name, out _))
            .DistinctBy(region => region.Name)
            .Select(region => TryGet(region.Name, cultureInfo))
            .Where(countryInfo => countryInfo != null)
            .OrderBy(countryInfo => countryInfo!.TranslatedName)
            .ToArray();

        return countryInfos!;
    }
}
