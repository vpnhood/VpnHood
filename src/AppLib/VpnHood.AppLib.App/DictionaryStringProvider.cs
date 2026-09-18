using System.Globalization;
using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib;

// Words a UI pushed at configure time, in the language it was speaking. The culture is ignored on
// purpose: these are one set of words, not a translation table, and the UI pushes again when its
// language changes. A key it has not got falls through to the package, then to English.
internal class DictionaryStringProvider(IReadOnlyDictionary<string, string> texts) : IStringProvider
{
    public string? GetString(CultureInfo culture, string key)
    {
        _ = culture;
        var text = texts.GetValueOrDefault(key);
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
