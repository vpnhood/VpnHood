using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppUi.Common;

// Country names in the language the words are in, out of the asset store (countries/<language>.json,
// a country's code to its name): the app's API carries codes alone, and the device's own data names a
// country in that country's language whatever the UI's (RegionInfo.DisplayName). Strings reads them
// with the words, as a row asks for a name while it is drawn and a page in a browser cannot wait for a
// file. A language or a country the store has no name for falls back to English, then to the caller's
// name (a location's own, from the server), then to the code.

/// <summary>
/// Provides the countries' names in the language last asked for, else in English, from the UI's content store; the
/// names fall back apart from the words.
/// </summary>
public static class CountryNames
{
    private const string Folder = "countries";
    private const string EnglishName = "en";

    private static IReadOnlyDictionary<string, string> _english = ReadOnlyDictionary<string, string>.Empty;
    private static IReadOnlyDictionary<string, string> _names = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>
    /// Gets a country's name in the language last set, else in English, else the fallback, else the code.
    /// </summary>
    /// <param name="countryCode">The country's two-letter code.</param>
    /// <param name="fallback">The name to use where the store has none, as a location's own, or <see langword="null"/>.</param>
    /// <returns>The name.</returns>
    public static string Get(string countryCode, string? fallback = null)
    {
        var code = countryCode.ToUpperInvariant();
        return _names.GetValueOrDefault(code) ?? _english.GetValueOrDefault(code) ?? fallback ?? countryCode;
    }

    internal static async Task LoadEnglishAsync(IAssetProvider assets, CancellationToken cancellationToken)
    {
        _english = await LoadFile(assets, EnglishName, cancellationToken).ConfigureAwait(false)
                   ?? ReadOnlyDictionary<string, string>.Empty;
    }

    // A culture the store has no file for falls back to the language alone (pt-BR to pt), as the words do.
    internal static async Task LoadAsync(IAssetProvider assets, CultureInfo culture, CancellationToken cancellationToken)
    {
        _names = await LoadFile(assets, culture.Name, cancellationToken).ConfigureAwait(false)
                 ?? await LoadFile(assets, culture.TwoLetterISOLanguageName, cancellationToken).ConfigureAwait(false)
                 ?? ReadOnlyDictionary<string, string>.Empty;
    }

    private static async Task<IReadOnlyDictionary<string, string>?> LoadFile(IAssetProvider assets, string cultureName,
        CancellationToken cancellationToken)
    {
        await using var stream = await assets.TryOpenReadAsync($"{Folder}/{cultureName}.json", cancellationToken).ConfigureAwait(false);
        return stream == null
            ? null
            : await JsonSerializer.DeserializeAsync(stream, AssetJsonContext.Default.DictionaryStringString, cancellationToken).ConfigureAwait(false);
    }
}
