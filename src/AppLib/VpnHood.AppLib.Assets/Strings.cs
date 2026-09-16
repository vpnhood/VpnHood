using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace VpnHood.AppLib.Assets;

// The UI's words: the locale files (Locales/*.json - en.json the source a person edits, the rest
// vhtranslator's), embedded in this assembly, so a word is there before any folder is named and
// wherever this assembly runs. Strings.g.cs names every key of en.json as a member
// (_sync-assets.ps1), so a page asks for a string in C# and a key that disappears is a compile
// error. An app built on this package lays its own words over these through AddSource.
//
// One instance, Current, and the pages bind to its members rather than reading them once:
//
//     Text="{Binding Connect, Source={x:Static res:Strings.Current}}"
//
// so a language chosen on the paired phone reaches a TV that is already showing the page - a
// property change with an empty name, which every binding on this object takes as its own.
public sealed partial class Strings : INotifyPropertyChanged
{
    private const string LocalePrefix = "VpnHood.AppLib.Assets.Locales.";
    private const string LocaleSuffix = ".json";
    private const string EnglishName = "en";

    private static readonly IReadOnlyDictionary<string, string> English =
        LoadEmbedded(EnglishName) ?? throw new InvalidOperationException("Locales/en.json is not in this assembly.");

    private static readonly List<IStringSource> Sources = [];

    private IReadOnlyDictionary<string, string> _texts = English;
    private string _cultureName = EnglishName;

    public static Strings Current { get; } = new();

    // The languages there are words for: this assembly's locale files and every source added,
    // which is what the UI declares to the app at start (ConfigParams.AvailableCultures), so the
    // app's language list and its best-culture choice are made from the words that exist.
    public static IReadOnlyList<string> AvailableCultures {
        get {
            var embedded = typeof(Strings).Assembly.GetManifestResourceNames()
                .Where(x => x.StartsWith(LocalePrefix, StringComparison.Ordinal) && x.EndsWith(LocaleSuffix, StringComparison.Ordinal))
                .Select(x => x[LocalePrefix.Length..^LocaleSuffix.Length]);

            lock (Sources)
                return [.. embedded.Concat(Sources.SelectMany(x => x.Cultures)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private Strings()
    {
    }

    // A locale file as it is, for a UI that reads its own words rather than these: the web UI
    // fetches /assets/locales/<culture>.json from the app's web server, which serves it from here -
    // so the words are in the package once, whichever UI shows them. Null for a language there is
    // no file for.
    public static Stream? OpenLocaleFile(string cultureName)
    {
        return typeof(Strings).Assembly.GetManifestResourceStream(LocalePrefix + cultureName + LocaleSuffix);
    }

    // Words laid over this assembly's: a language it does not ship, or lines said another way.
    // The last source added speaks first. Takes effect at once, on a page already showing.
    public static void AddSource(IStringSource source)
    {
        lock (Sources)
            Sources.Insert(0, source);
        Current.Reload();
    }

    // The language to show - the app's answer, the person's choice or the device's. A culture we
    // ship no file for falls back to the language alone (pt-PT to pt), then to English.
    // Returns whether the words changed, so a caller can rebuild text it has already composed.
    public bool SetCulture(CultureInfo culture)
    {
        if (_cultureName == culture.Name)
            return false;

        _cultureName = culture.Name;
        Reload(culture);
        return true;
    }

    private void Reload()
    {
        Reload(CultureInfo.GetCultureInfo(_cultureName));
    }

    private void Reload(CultureInfo culture)
    {
        var texts = Load(culture.Name) ?? Load(culture.TwoLetterISOLanguageName);
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
        if (_texts.TryGetValue(key, out var text))
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

    // The words of a language: this assembly's file, with every source's words laid over it. A
    // language only a source has is that source over English. Null when nobody has it.
    private static IReadOnlyDictionary<string, string>? Load(string cultureName)
    {
        var texts = LoadEmbedded(cultureName);

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

    private static IReadOnlyDictionary<string, string>? LoadEmbedded(string cultureName)
    {
        using var stream = typeof(Strings).Assembly.GetManifestResourceStream(LocalePrefix + cultureName + LocaleSuffix);
        return stream == null
            ? null
            : JsonSerializer.Deserialize(stream, LocaleJsonContext.Default.DictionaryStringString);
    }
}
