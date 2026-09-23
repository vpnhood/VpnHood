using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Common;

// The UI's words: the locale files of the asset store (locales/<culture>.json - en.json the source
// a person edits, the rest vhtranslator's), read through the provider the head hands in - a folder
// in process, the app's web host from a browser page - so the words ship once, as files, the way
// the pictures do, and a brand is a package swap. Strings.g.cs names every key of en.json as a
// member (_sync-assets.ps1), so a page asks for a string in C# and a key that disappears is a
// compile error. An app built on this package lays its own words over these through AddSourceAsync.
//
// Loaded by InitAsync before the first view (IAvaloniaUi.PrepareContentAsync); a store with no
// words is a build with no store, and says so with the file's path.
//
// One instance, Current, and the pages bind to its members rather than reading them once:
//
//     Text="{Binding Connect, Source={x:Static res:Strings.Current}}"
//
// so a language chosen on the paired phone reaches a TV that is already showing the page - a
// property change with an empty name, which every binding on this object takes as its own.
public sealed partial class Strings : INotifyPropertyChanged
{
    private const string LocalesFolder = "locales";
    private const string EnglishName = "en";

    // The languages the store has words for, written beside them by _sync-assets.ps1: a list
    // rather than a listing, because a provider answers by name and never enumerates.
    private const string IndexPath = $"{LocalesFolder}/index.json";

    private static IAssetProvider? _assets;
    private static IReadOnlyDictionary<string, string>? _english;
    private static IReadOnlyList<string> _shippedCultures = [];
    private static readonly List<IStringSource> Sources = [];

    private IReadOnlyDictionary<string, string>? _texts;
    private string _cultureName = EnglishName;

    public static Strings Current { get; } = new();

    private static IAssetProvider Assets => _assets ?? throw NotLoaded();
    private static IReadOnlyDictionary<string, string> English => _english ?? throw NotLoaded();

    private static InvalidOperationException NotLoaded()
    {
        return new InvalidOperationException($"The words have not been loaded: {nameof(Strings)}.{nameof(InitAsync)} runs before the first view.");
    }

    // The words, out of the store the head hands in: English, which every other language falls
    // back to, and the languages there are files for. Once, before the first view.
    public static async Task InitAsync(IAssetProvider assets, CancellationToken cancellationToken)
    {
        _assets = assets;

        // English is the one language that must be there - every other falls back to it - so it is
        // asked for rather than looked up, and a store without it says so by name.
        await using (var english = await assets.OpenReadAsync(FilePathOf(EnglishName), cancellationToken).Vhc())
            _english = await JsonSerializer.DeserializeAsync(english, AssetJsonContext.Default.DictionaryStringString, cancellationToken).Vhc()
                       ?? throw new InvalidOperationException($"{FilePathOf(EnglishName)} holds no words.");
        _shippedCultures = await AssetIndex.ReadAsync(assets, IndexPath, cancellationToken).Vhc();
        Current._texts = _english;
    }

    // The languages there are words for: the store's, by its index, and every source added -
    // which is what the UI declares to the app at start (ConfigParams.AvailableCultures), so the
    // app's language list and its best-culture choice are made from the words that exist.
    public static IReadOnlyList<string> AvailableCultures {
        get {
            lock (Sources)
                return [.. _shippedCultures.Concat(Sources.SelectMany(x => x.Cultures)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private Strings()
    {
    }

    // The language the words are in: what SetCultureAsync last took, English at first.
    public string CultureName => _cultureName;

    // Words laid over the store's: a language it does not ship, or lines said another way. The
    // last source added speaks first. Takes effect as soon as the words are reloaded, on a page
    // already showing.
    public static async Task AddSourceAsync(IStringSource source, CancellationToken cancellationToken)
    {
        lock (Sources)
            Sources.Insert(0, source);
        await Current.Reload(CultureInfo.GetCultureInfo(Current._cultureName), cancellationToken).Vhc();
    }

    // The language to show - the app's answer, the person's choice or the device's. A culture we
    // ship no file for falls back to the language alone (pt-PT to pt), then to English.
    // Returns whether the words changed, so a caller can rebuild text it has already composed.
    public async Task<bool> SetCultureAsync(CultureInfo culture, CancellationToken cancellationToken)
    {
        if (_cultureName == culture.Name)
            return false;

        _cultureName = culture.Name;
        await Reload(culture, cancellationToken).Vhc();
        return true;
    }

    private async Task Reload(CultureInfo culture, CancellationToken cancellationToken)
    {
        var texts = await Load(culture.Name, cancellationToken).Vhc()
                    ?? await Load(culture.TwoLetterISOLanguageName, cancellationToken).Vhc();
        _texts = texts ?? English;
        // the direction of the words shown, not of the culture asked for: a language we have no
        // file for is shown in English, which runs left to right
        IsRightToLeft = texts != null && culture.TextInfo.IsRightToLeft;

        // no name: every binding on this object reads its property again
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    // The way the words run, which the web UI takes from Vuetify's rtl map (ar and fa). The UI's
    // root mirrors its layout by it - the panels, the grids, the margins, the drawer's side. Text
    // and images are never mirrored, and the spots the web UI pins with dir="ltr" or a locale
    // provider - addresses, keys, figures - are pinned the same way.
    public bool IsRightToLeft { get; private set; }

    // The chevrons that point the way through the pages - Util.getLocalizedRightChevron and its
    // left twin. A glyph is text, which the mirroring leaves as it is, so the glyph itself turns.
    public string ChevronForward => IsRightToLeft ? Mdi.ChevronLeft : Mdi.ChevronRight;
    public string ChevronBack => IsRightToLeft ? Mdi.ChevronRight : Mdi.ChevronLeft;

    // The web UI hardcodes this unit in ConnectionInfo.vue, so it has no key to follow.
    public string Mbps => "Mbps";

    // The address the phone dials, before the listener has one. The web UI's dialog has no such
    // moment - it asks the app that is already running - so there is no key for it either.
    public string RemoteAccessStarting => "Starting…";

    private string Get(string key)
    {
        if ((_texts ?? English).TryGetValue(key, out var text))
            return text;

        // Strings.g.cs is generated from en.json, so a key missing from English means the two have
        // drifted: show the key rather than nothing, and trip a developer's build.
        Debug.Assert(English.ContainsKey(key), $"No locale key '{key}'. Run _sync-assets.ps1.");
        return English.GetValueOrDefault(key, key);
    }

    // vue-i18n's named placeholders: "Connected from {address}."
    private string Get(string key, params (string Name, object Value)[] arguments)
    {
        var text = Get(key);
        foreach (var (name, value) in arguments)
            text = text.Replace($"{{{name}}}", Convert.ToString(value, CultureInfo.CurrentCulture));
        return text;
    }

    // The words of a language: the store's file, with every source's words laid over it. A
    // language only a source has is that source over English. Null when nobody has it.
    private static async Task<IReadOnlyDictionary<string, string>?> Load(string cultureName, CancellationToken cancellationToken)
    {
        var texts = await LoadFile(cultureName, cancellationToken).Vhc();

        IStringSource[] sources;
        lock (Sources)
            sources = [.. Sources];

        // the first source added is laid last, so the last added speaks first
        foreach (var source in sources.Reverse()) {
            var extra = source.Load(cultureName);
            if (extra == null)
                continue;

            var merged = new Dictionary<string, string>(texts ?? English, StringComparer.Ordinal);
            foreach (var (key, text) in extra)
                merged[key] = text;
            texts = merged;
        }

        return texts;
    }

    private static string FilePathOf(string cultureName)
    {
        return $"{LocalesFolder}/{cultureName}.json";
    }

    private static async Task<IReadOnlyDictionary<string, string>?> LoadFile(string cultureName, CancellationToken cancellationToken)
    {
        await using var stream = await Assets.TryOpenReadAsync(FilePathOf(cultureName), cancellationToken).Vhc();
        return stream == null
            ? null
            : await JsonSerializer.DeserializeAsync(stream, AssetJsonContext.Default.DictionaryStringString, cancellationToken).Vhc();
    }
}
