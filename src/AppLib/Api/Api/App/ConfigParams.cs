namespace VpnHood.AppLib.Api.App;

public class ConfigParams
{
    public IReadOnlyList<string> AvailableCultures { get; init; } = [];

    // The UI's own words for the app's own text, keyed as the locale files key them (CONNECT), in
    // the language the UI is showing. A key left out keeps the word the app already has.
    public IReadOnlyDictionary<string, string>? Strings { get; init; }
}
