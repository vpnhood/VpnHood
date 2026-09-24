namespace VpnHood.AppUi.Common;

// Words from outside this assembly, laid over the ones it carries: an app built on these packages
// adds a language they do not ship, or says a few lines its own way - its name in place of ours -
// without a build of ours. The same shape as a locale file: the keys of en.json, a text for each
// the source has. Registered with Strings.AddSourceAsync, before or after the UI is up.
public interface IStringSource
{
    // The languages this source has, as culture names (fa, pt-BR).
    IReadOnlyList<string> Cultures { get; }

    // The words of a language; null when the source has none for it.
    IReadOnlyDictionary<string, string>? Load(string cultureName);
}
