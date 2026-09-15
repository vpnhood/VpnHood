using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Platform;

namespace VpnHood.AppLib.AvaloniaUI.Resources;

// The UI's words: the web UI's own locale files, read from the assets folder (locales/*.json -
// en.json the source a person edits, the rest vhtranslator's) because those files are the one
// place the wording lives, and the web UI reads the very same ones. Strings.g.cs names every key
// of en.json as a member (_sync-locales.ps1), so a page asks for a string in C# and a key that
// disappears is a compile error.
//
// One instance, Current, and the pages bind to its members rather than reading them once:
//
//     Text="{Binding Connect, Source={x:Static res:Strings.Current}}"
//
// so a language chosen on the paired phone reaches a TV that is already showing the page - a
// property change with an empty name, which every binding on this object takes as its own.
public sealed partial class Strings : INotifyPropertyChanged
{
    private static readonly IReadOnlyDictionary<string, string> English =
        Load("en") ?? throw new InvalidOperationException("Assets/Locales/en.json is not in this assembly.");

    private IReadOnlyDictionary<string, string> _texts = English;
    private string _cultureName = "en";

    public static Strings Current { get; } = new();

    // The languages this UI has, by the locale files in the assets folder: what the web UI declares
    // to the app at start (ConfigParams.AvailableCultures), so the app's language list and its
    // best-culture choice are made from the words that actually exist there.
    public static IReadOnlyList<string> AvailableCultures { get; } =
        [.. AppAssets.FileNames("locales", "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];

    public event PropertyChangedEventHandler? PropertyChanged;

    private Strings()
    {
    }

    // The language to show - the app's answer, the person's choice or the device's. A culture we
    // ship no file for falls back to the language alone (pt-PT to pt), then to English.
    // Returns whether the words changed, so a caller can rebuild text it has already composed.
    public bool SetCulture(CultureInfo culture)
    {
        if (_cultureName == culture.Name)
            return false;

        _cultureName = culture.Name;
        _texts = Load(culture.Name) ?? Load(culture.TwoLetterISOLanguageName) ?? English;

        // no name: every binding on this object reads its property again
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        return true;
    }

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
        Debug.Assert(English.ContainsKey(key), $"No locale key '{key}'. Run _sync-locales.ps1.");
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

    private static IReadOnlyDictionary<string, string>? Load(string cultureName)
    {
        var json = AppAssets.ReadText($"locales/{cultureName}.json");
        return json == null
            ? null
            : JsonSerializer.Deserialize(json, LocaleJsonContext.Default.DictionaryStringString);
    }
}

// The heads publish trimmed, so the shape these files deserialize into is declared rather than
// discovered.
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class LocaleJsonContext : JsonSerializerContext;
