namespace VpnHood.AppLib.Api.App;

/// <summary>Represents what the UI gives the app (<see cref="IAppApi.Configure"/>).</summary>
public class ConfigParams
{
    /// <summary>Gets the languages the UI offers, by their culture names. The default is none.</summary>
    public IReadOnlyList<string> AvailableCultures { get; init; } = [];

    /// <summary>
    /// Gets the UI's own words for the app's own text, keyed as the locale files key them (<c>CONNECT</c>), in the language
    /// the UI shows, or <see langword="null"/>; a key left out takes the app's built-in word.
    /// </summary>
    /// <remarks><see langword="null"/>, or no words at all, keeps the words given before.</remarks>
    public IReadOnlyDictionary<string, string>? Strings { get; init; }
}
