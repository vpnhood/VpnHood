using System.Collections.Concurrent;
using System.Globalization;
using Nager.Country;
using Nager.Country.Translation;

namespace VpnHood.AppUi.Common;

// Country names in the language the words are in (Strings), out of Nager's translations: the app's
// API carries codes alone, and the device's own data names a country in that country's language
// whatever the UI's (RegionInfo.DisplayName). A language Nager lacks falls back to English, then to
// the caller's name (a location's own, from the server), then to the code. The home asks on every
// refresh, so each answer is kept.
public static class CountryNames
{
    private const string EnglishName = "en";

    // Nager's provider scans every type of every assembly the app has loaded for its languages - a
    // fifth of a second of a phone's start - so it is made on first use, and the start asks for no
    // name unless the home shows one (a location row looks its name up as it is drawn). Add no
    // warm-up on a worker: that scan contends with Avalonia's own setup and made the start slower.
    private static readonly Lazy<TranslationProvider> Provider = new(() => new TranslationProvider());
    private static readonly ConcurrentDictionary<(string CountryCode, string CultureName), string?> Names = new();

    public static string Get(string countryCode, string? fallback = null)
    {
        return Find(countryCode, Strings.Current.CultureName) ?? Find(countryCode, EnglishName) ?? fallback ?? countryCode;
    }

    private static string? Find(string countryCode, string cultureName)
    {
        return Names.GetOrAdd((countryCode.ToUpperInvariant(), cultureName), key => Translate(key.CountryCode, key.CultureName));
    }

    private static string? Translate(string countryCode, string cultureName)
    {
        // a number parses too, so the code must also name a member
        return Enum.TryParse<Alpha2Code>(countryCode, ignoreCase: true, out var alpha2Code) && Enum.IsDefined(alpha2Code)
            ? Provider.Value.GetCountryTranslatedName(alpha2Code, CultureInfo.GetCultureInfo(cultureName))
            : null;
    }
}
