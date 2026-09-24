using System.Globalization;

namespace VpnHood.AppLib.Abstractions;

// Translated words for the text the app draws for itself, given to AppResources by a head that has
// them. The app library is a skeleton and names no source of its own, so without one it speaks the
// English of its resources. Asked per word, so a language chosen while the app runs is answered.
public interface IStringProvider
{
    // By the key the locale files use (CONNECT, MSG_UNSUPPORTED_CONTENT); null if it has neither
    // the key nor the language, and the caller keeps its own word.
    string? GetString(CultureInfo culture, string key);
}
