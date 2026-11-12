using System.Globalization;

namespace VpnHood.AppLib.Abstractions;

/// <summary>
/// Represents the translated words for the text the app draws for itself, which a head that has them gives the app.
/// </summary>
/// <remarks>
/// The app library names no source of its own, so without one it speaks the English of its resources. It is asked per
/// word, so a language chosen while the app runs is answered.
/// </remarks>
public interface IStringProvider
{
    /// <summary>Gets a word in a language.</summary>
    /// <param name="culture">The language.</param>
    /// <param name="key">The word's key, as the locale files have it, as <c>CONNECT</c>.</param>
    /// <returns>
    /// The word, or <see langword="null"/> where the provider has neither the key nor the language, and the caller keeps
    /// its own.
    /// </returns>
    string? GetString(CultureInfo culture, string key);
}
