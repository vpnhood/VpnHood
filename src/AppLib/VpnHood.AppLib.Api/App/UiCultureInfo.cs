using System.Globalization;
using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.App;

/// <summary>Represents a language the UI can show: its culture name, and its own name for itself.</summary>
/// <param name="cultureInfo">The language's culture.</param>
public class UiCultureInfo(CultureInfo cultureInfo)
{
    /// <summary>Initializes a new instance of the <see cref="UiCultureInfo"/> class.</summary>
    /// <param name="code">The language's culture name, as <c>fr</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="code"/> is <see langword="null"/>.</exception>
    /// <exception cref="CultureNotFoundException"><paramref name="code"/> names no culture.</exception>
    public UiCultureInfo(string code)
        : this(new CultureInfo(code))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UiCultureInfo"/> class, as JSON from the other side of the API
    /// reads it: with the device's own name for the language, not one the reader's culture data would make.
    /// </summary>
    /// <param name="code">The language's culture name, as <c>fr</c>.</param>
    /// <param name="nativeName">The language's name for itself.</param>
    /// <exception cref="ArgumentNullException"><paramref name="code"/> is <see langword="null"/>.</exception>
    /// <exception cref="CultureNotFoundException"><paramref name="code"/> names no culture.</exception>
    [JsonConstructor]
    public UiCultureInfo(string code, string nativeName)
        : this(new CultureInfo(code))
    {
        NativeName = nativeName;
    }

    /// <summary>Gets the language's culture name, as <c>fr</c>.</summary>
    public string Code { get; } = cultureInfo.Name;

    /// <summary>Gets the language's name for itself, as <c>français</c>.</summary>
    public string NativeName { get; } = cultureInfo.NativeName;
}
