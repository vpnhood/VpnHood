namespace VpnHood.AppUi.Common;

// Words from outside this assembly, laid over the ones it carries: an app built on these packages
// adds a language they do not ship, or says a few lines its own way - its name in place of ours -
// without a build of ours. The same shape as a locale file: the keys of en.json, a text for each
// the source has. Registered with Strings.AddSourceAsync, before or after the UI is up.

/// <summary>
/// Defines words from outside this assembly, laid over the ones it carries: a language it does not ship, or lines said
/// another way.
/// </summary>
/// <remarks>It has the shape of a locale file. It is added with <see cref="Strings.AddSourceAsync"/>, before or after the UI is up.</remarks>
public interface IStringSource
{
    /// <summary>Gets the languages this source has, as culture names (<c>fa</c>, <c>pt-BR</c>).</summary>
    IReadOnlyList<string> Cultures { get; }

    /// <summary>Gets the words of a language.</summary>
    /// <param name="cultureName">The language's culture name.</param>
    /// <returns>The words, by key, or <see langword="null"/> where the source has none for it.</returns>
    IReadOnlyDictionary<string, string>? Load(string cultureName);
}
