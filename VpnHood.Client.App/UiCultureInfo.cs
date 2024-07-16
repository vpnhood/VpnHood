using System.Globalization;

namespace VpnHood.Client.App;

public class UiCultureInfo(CultureInfo cultureInfo)
{
    public UiCultureInfo(string code)
        : this(new CultureInfo(code))
    {
    }

    public string Code { get; } = cultureInfo.TwoLetterISOLanguageName;
    public string NativeName { get; } = cultureInfo.NativeName;
}