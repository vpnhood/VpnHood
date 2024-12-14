using System.Globalization;

namespace VpnHood.AppLibs;

public class UiCultureInfo(CultureInfo cultureInfo)
{
    public UiCultureInfo(string code)
        : this(new CultureInfo(code))
    {
    }

    public string Code { get; } = cultureInfo.Name;
    public string NativeName { get; } = cultureInfo.NativeName;
}