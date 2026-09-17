using System.Globalization;
using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.App;

public class UiCultureInfo(CultureInfo cultureInfo)
{
    public UiCultureInfo(string code)
        : this(new CultureInfo(code))
    {
    }

    // The JSON of one, read back by a UI on the other side of the API: the device's own native
    // name, not one the reader's culture data would make.
    [JsonConstructor]
    public UiCultureInfo(string code, string nativeName)
        : this(new CultureInfo(code))
    {
        NativeName = nativeName;
    }

    public string Code { get; } = cultureInfo.Name;
    public string NativeName { get; } = cultureInfo.NativeName;
}
